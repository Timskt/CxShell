using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CxShell.Models;
using Renci.SshNet;

namespace CxShell.Services;

public class ServerMonitorService : IDisposable
{
    private enum MonitorTargetKind
    {
        Linux,
        Windows,
        Darwin
    }

    private const string LinuxSectionSeparator = "---SEP---";

    /// <summary>
    /// macOS collector. It emits the same CPU|MEM|DISK|NET lines the Windows script
    /// does, so both share one parser. vm_stat counts pages whose size only appears in
    /// its header, and "available" is free + inactive + speculative because macOS keeps
    /// retired pages reclaimable rather than zeroed. df is filtered to real volumes:
    /// APFS otherwise reports every system snapshot and asset mount as its own disk.
    /// </summary>
    private const string DarwinMonitorScript = """
top -l 1 -n 0 | awk -F'[:,%]+' '/CPU usage/ { for (i=1; i<=NF; i++) if ($i ~ /idle/) printf "CPU|0|%.1f\n", 100 - $(i-1) }'

pagesize=$(vm_stat | awk 'NR==1 { for (i=1; i<=NF; i++) if ($i == "of") { gsub(/[^0-9]/, "", $(i+1)); print $(i+1); exit } }')
free=$(vm_stat | awk -F: '/^Pages free/ {gsub(/[ \t.]/, "", $2); print $2}')
inactive=$(vm_stat | awk -F: '/^Pages inactive/ {gsub(/[ \t.]/, "", $2); print $2}')
spec=$(vm_stat | awk -F: '/^Pages speculative/ {gsub(/[ \t.]/, "", $2); print $2}')
echo "MEM|$(sysctl -n hw.memsize | awk '{printf "%d\n", $1/1024}')|$(( (${free:-0} + ${inactive:-0} + ${spec:-0}) * ${pagesize:-4096} / 1024 ))"

df -P | awk 'NR>1 && $1 ~ /^\/dev\// {
  m = $6
  if (m != "/" && m !~ /^\/Volumes\// && m != "/System/Volumes/Data") next
  printf "DISK|%s|%d|%d\n", m, $2 * 512, $4 * 512
}'

rx() { netstat -ibn | awk 'NR>1 && $1 !~ /^lo/ && $NF != "-" { if (!seen[$1]++) r += $7 } END { print r+0 }'; }
tx() { netstat -ibn | awk 'NR>1 && $1 !~ /^lo/ && $NF != "-" { if (!seen[$1]++) t += $10 } END { print t+0 }'; }
r1=$(rx); t1=$(tx); sleep 1; r2=$(rx); t2=$(tx)
echo "NET|$(( r2 > r1 ? r2 - r1 : 0 ))|$(( t2 > t1 ? t2 - t1 : 0 ))"
""";
    private const string WindowsMonitorScript = """
$ErrorActionPreference = 'SilentlyContinue'
$ProgressPreference = 'SilentlyContinue'
$WarningPreference = 'SilentlyContinue'
$InformationPreference = 'SilentlyContinue'

function To-Int64Value($value) {
    if ($null -eq $value) { return 0 }
    return [int64]$value
}

function To-DoubleValue($value) {
    if ($null -eq $value) { return 0.0 }
    return [double]$value
}

$invariant = [System.Globalization.CultureInfo]::InvariantCulture

$cpuCounters = @(Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -ErrorAction SilentlyContinue)
$totalCpu = $cpuCounters | Where-Object { $_.Name -eq '_Total' } | Select-Object -First 1
if ($null -ne $totalCpu) {
    $cpuValue = (To-DoubleValue $totalCpu.PercentProcessorTime).ToString($invariant)
    Write-Output ("CPU|0|{0}" -f $cpuValue)
}

$coreIndex = 1
foreach ($cpu in ($cpuCounters | Where-Object { $_.Name -ne '_Total' } | Sort-Object Name)) {
    $cpuValue = (To-DoubleValue $cpu.PercentProcessorTime).ToString($invariant)
    Write-Output ("CPU|{0}|{1}" -f $coreIndex, $cpuValue)
    $coreIndex++
}

if ($null -eq $totalCpu) {
    $processors = @(Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue)
    $loadValues = @($processors | Where-Object { $null -ne $_.LoadPercentage } | ForEach-Object { To-DoubleValue $_.LoadPercentage })
    if ($loadValues.Count -gt 0) {
        $sumCpu = 0.0
        foreach ($value in $loadValues) { $sumCpu += $value }
        $avgCpu = ($sumCpu / [Math]::Max(1, $loadValues.Count)).ToString($invariant)
        Write-Output ("CPU|0|{0}" -f $avgCpu)

        $coreIndex = 1
        foreach ($value in $loadValues) {
            Write-Output ("CPU|{0}|{1}" -f $coreIndex, $value.ToString($invariant))
            $coreIndex++
        }
    }
}

$os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -ne $os) {
    Write-Output ("MEM|{0}|{1}" -f (To-Int64Value $os.TotalVisibleMemorySize), (To-Int64Value $os.FreePhysicalMemory))
}

$rx = [int64]0
$tx = [int64]0
$networkCounters = Get-CimInstance Win32_PerfFormattedData_Tcpip_NetworkInterface -ErrorAction SilentlyContinue
foreach ($nic in $networkCounters) {
    $name = [string]$nic.Name
    if ($name -match 'Loopback|isatap|Teredo') { continue }
    $rx += To-Int64Value $nic.BytesReceivedPersec
    $tx += To-Int64Value $nic.BytesSentPersec
}
Write-Output ("NET|{0}|{1}" -f $rx, $tx)

Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Output ("DISK|{0}|{1}|{2}" -f $_.DeviceID, (To-Int64Value $_.Size), (To-Int64Value $_.FreeSpace))
}

Get-CimInstance Win32_PerfFormattedData_PerfDisk_LogicalDisk -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne '_Total' -and $_.Name -notmatch '^HarddiskVolume' } |
    ForEach-Object {
        Write-Output ("DIO|{0}|{1}|{2}" -f $_.Name, (To-Int64Value $_.DiskReadBytesPersec), (To-Int64Value $_.DiskWriteBytesPersec))
    }

exit 0
""";

    private SshClient? _sshClient;
    private SshConnectionContext? _sshConnectionContext;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private DateTime _lastSampleTime;
    private MonitorTargetKind _targetKind = MonitorTargetKind.Linux;
    private Func<string, TimeSpan, CancellationToken, Task<string>>? _commandRunner;
    private bool _ownsSshClient;
    private int _refreshIntervalSeconds = SessionInfo.DefaultSshMonitorRefreshIntervalSeconds;
    private bool _enableNetworkLatencyProbe;

    private List<long[]>? _prevCpuStat;
    private Dictionary<string, (long rx, long tx)>? _prevNetStat;
    private Dictionary<string, (long readSectors, long writeSectors)>? _prevDiskStat;
    private readonly object _debugLogLock = new();
    private bool _hasLoggedWindowsScript;
    private bool _hasProbedTargetKind;

    public bool IsMonitoring => _monitorTask != null && !_monitorTask.IsCompleted;
    public static string DebugLogPath => Path.Combine(GetDebugLogDirectory(), "server-monitor-debug.log");

    public event Action<MonitorSnapshot, long>? DataUpdated;
    public event Action<string, long, bool>? ErrorOccurred;

    public async Task StartAsync(
        SessionInfo session,
        string? password,
        Func<string, TimeSpan, CancellationToken, Task<string>>? commandRunner = null,
        bool isWindowsOpenSsh = false,
        int refreshIntervalSeconds = SessionInfo.DefaultSshMonitorRefreshIntervalSeconds,
        bool enableNetworkLatencyProbe = false,
        long callbackGeneration = 0)
    {
        Stop();
        _commandRunner = commandRunner;
        _ownsSshClient = commandRunner == null;
        _refreshIntervalSeconds = Math.Clamp(
            refreshIntervalSeconds,
            SessionInfo.MinSshMonitorRefreshIntervalSeconds,
            SessionInfo.MaxSshMonitorRefreshIntervalSeconds);
        _enableNetworkLatencyProbe = enableNetworkLatencyProbe;
        _targetKind = isWindowsOpenSsh ? MonitorTargetKind.Windows : MonitorTargetKind.Linux;
        _hasLoggedWindowsScript = false;
        DebugLog($"start session={session.Username}@{session.Host}:{session.Port} protocol={session.Protocol} commandRunner={commandRunner != null} hintedTarget={_targetKind} refreshInterval={_refreshIntervalSeconds}s latencyProbe={_enableNetworkLatencyProbe}");

        if (commandRunner != null)
        {
            StartMonitorLoop(callbackGeneration);
            return;
        }

        var authMethods = SshAgentAuthService.CreateAuthenticationMethods(session, password);
        _sshConnectionContext = await Task.Run(
            () => ProxyConnectionFactory.CreateSshConnectionContext(session, authMethods));
        try
        {
            var connectionInfo = _sshConnectionContext.ConnectionInfo;
            SshAlgorithmPreferenceService.Apply(connectionInfo, session);
            _sshClient = new SshClient(connectionInfo)
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            };
            SshHostKeyTrustService.Shared.Attach(
                _sshClient,
                session.Host,
                session.Port,
                session.SshAcceptAndSaveHostKey);

            await ConnectWithRetryAsync(_sshClient, CancellationToken.None).ConfigureAwait(false);
            _targetKind = isWindowsOpenSsh || SshServerInfo.IsWindowsOpenSshServer(connectionInfo.ServerVersion)
                ? MonitorTargetKind.Windows
                : MonitorTargetKind.Linux;
            DebugLog($"ssh monitor connected serverVersion={connectionInfo.ServerVersion} detectedTarget={_targetKind}");
        }
        catch (Exception ex)
        {
            var displayMessage = SshServerInfo.BuildConnectionErrorMessage(ex);
            DebugLog($"ssh monitor connection failed message={displayMessage} exception={ex}");
            ErrorOccurred?.Invoke(
                string.Format(LocalizationService.Shared.Text("Monitor.ConnectionFailed"), displayMessage),
                callbackGeneration,
                true);
            _sshClient?.Dispose();
            _sshClient = null;
            _sshConnectionContext?.Dispose();
            _sshConnectionContext = null;
            return;
        }

        StartMonitorLoop(callbackGeneration);
    }

    public void Stop()
    {
        DebugLog("stop");
        _cts?.Cancel();
        try { _monitorTask?.Wait(TimeSpan.FromSeconds(3)); } catch { }
        if (_ownsSshClient)
        {
            _sshClient?.Disconnect();
            _sshClient?.Dispose();
            _sshConnectionContext?.Dispose();
        }

        _sshClient = null;
        _sshConnectionContext = null;
        _cts?.Dispose();
        _cts = null;
        _monitorTask = null;
        _lastSampleTime = default;
        _targetKind = MonitorTargetKind.Linux;
        _hasProbedTargetKind = false;
        _refreshIntervalSeconds = SessionInfo.DefaultSshMonitorRefreshIntervalSeconds;
        _enableNetworkLatencyProbe = false;
        _commandRunner = null;
        _ownsSshClient = false;
        _prevCpuStat = null;
        _prevNetStat = null;
        _prevDiskStat = null;
    }

    private void StartMonitorLoop(long callbackGeneration)
    {
        _lastSampleTime = default;
        _prevCpuStat = null;
        _prevNetStat = null;
        _prevDiskStat = null;

        _cts = new CancellationTokenSource();
        DebugLog($"monitor loop start target={_targetKind} ownsSshClient={_ownsSshClient} commandRunner={_commandRunner != null} callbackGeneration={callbackGeneration}");
        _monitorTask = Task.Run(() => MonitorLoop(_cts.Token, callbackGeneration));
    }

    /// <summary>
    /// The SSH banner only distinguishes Windows OpenSSH from everything else, so a
    /// macOS or BSD host was collected with /proc commands and silently produced
    /// nothing. One uname per session picks the right collector; a failed probe keeps
    /// the previous Linux default rather than losing monitoring altogether.
    /// </summary>
    private async Task DetectTargetKindAsync(CancellationToken ct, long callbackGeneration)
    {
        if (_targetKind == MonitorTargetKind.Windows || _hasProbedTargetKind)
            return;

        _hasProbedTargetKind = true;
        var output = await TryRunRemoteCommandAsync(
                "uname -s",
                TimeSpan.FromSeconds(5),
                ct,
                "uname-probe",
                callbackGeneration)
            .ConfigureAwait(false);
        if (output == null)
            return;

        var kernel = output.Trim();
        if (kernel.StartsWith("Darwin", StringComparison.OrdinalIgnoreCase))
            _targetKind = MonitorTargetKind.Darwin;

        DebugLog($"uname probe kernel={kernel} target={_targetKind}");
    }

    private static async Task ConnectWithRetryAsync(SshClient client, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        var delays = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };
        foreach (var delay in delays)
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            try
            {
                await Task.Run(() => client.Connect(), cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (client.IsConnected)
                    return;
            }
        }

        throw lastError ?? new InvalidOperationException("SSH monitor connection failed.");
    }

    private async Task MonitorLoop(CancellationToken ct, long callbackGeneration)
    {
        await DetectTargetKindAsync(ct, callbackGeneration).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_refreshIntervalSeconds));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var snapshot = await CollectAsync(ct, callbackGeneration).ConfigureAwait(false);
                if (snapshot != null)
                {
                    DebugLog($"collect success target={_targetKind} cpu={snapshot.CpuCores.Count} memory={snapshot.Memory != null} net={snapshot.NetworkSpeed != null} latencyMs={snapshot.NetworkLatencyMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "-"} disks={snapshot.DiskPartitions.Count} diskIo={snapshot.DiskIo.Count}");
                    DataUpdated?.Invoke(snapshot, callbackGeneration);
                }
                else
                {
                    DebugLog($"collect returned null target={_targetKind}");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                DebugLog($"collect exception target={_targetKind} exception={ex}");
                ErrorOccurred?.Invoke(
                    string.Format(LocalizationService.Shared.Text("Monitor.CollectFailed"), ex.Message),
                    callbackGeneration,
                    false);
            }
        }
    }

    private Task<MonitorSnapshot?> CollectAsync(CancellationToken ct, long callbackGeneration)
    {
        return _targetKind switch
        {
            MonitorTargetKind.Windows => CollectWindowsAsync(ct, callbackGeneration),
            MonitorTargetKind.Darwin => CollectDarwinAsync(ct, callbackGeneration),
            _ => CollectLinuxAsync(ct, callbackGeneration)
        };
    }

    private async Task<MonitorSnapshot?> CollectLinuxAsync(CancellationToken ct, long callbackGeneration)
    {
        if (!HasRemoteCommandSource())
            return null;

        var cmd =
            $"cat /proc/stat; echo '{LinuxSectionSeparator}'; " +
            $"cat /proc/meminfo; echo '{LinuxSectionSeparator}'; " +
            $"cat /proc/net/dev; echo '{LinuxSectionSeparator}'; " +
            $"cat /proc/diskstats; echo '{LinuxSectionSeparator}'; " +
            $"df -P";

        var output = await TryRunRemoteCommandAsync(
                cmd,
                TimeSpan.FromSeconds(5),
                ct,
                "linux-monitor",
                callbackGeneration)
            .ConfigureAwait(false);
        if (output == null)
            return null;

        var now = DateTime.Now;
        var elapsed = _lastSampleTime == default ? _refreshIntervalSeconds : (now - _lastSampleTime).TotalSeconds;
        _lastSampleTime = now;

        var normalizedOutput = output.Replace("\r\n", "\n", StringComparison.Ordinal);
        var sections = normalizedOutput.Split($"\n{LinuxSectionSeparator}\n", StringSplitOptions.None);
        if (sections.Length < 5)
            sections = normalizedOutput.Split($"{LinuxSectionSeparator}\n", StringSplitOptions.None);

        if (sections.Length < 5)
        {
            DebugLog($"linux parse skipped: expected 5 sections, got {sections.Length}. output={PreviewForLog(normalizedOutput)}");
            return null;
        }

        var currCpuStat = LinuxProcParser.ParseProcStat(sections[0]);
        List<CpuCoreInfo> cpuCores;
        if (_prevCpuStat != null)
            cpuCores = LinuxProcParser.CalculateCpuUsage(_prevCpuStat, currCpuStat);
        else
            cpuCores = new List<CpuCoreInfo>();
        _prevCpuStat = currCpuStat;

        var memory = LinuxProcParser.ParseProcMeminfo(sections[1]);

        var currNetStat = LinuxProcParser.ParseProcNetDev(sections[2]);
        NetworkSpeed? netSpeed = null;
        if (_prevNetStat != null)
            netSpeed = LinuxProcParser.CalculateNetworkSpeed(_prevNetStat, currNetStat, elapsed);
        _prevNetStat = currNetStat;

        var currDiskStat = LinuxProcParser.ParseProcDiskstats(sections[3]);
        List<DiskIoInfo> diskIo;
        if (_prevDiskStat != null)
            diskIo = LinuxProcParser.CalculateDiskIo(_prevDiskStat, currDiskStat, elapsed);
        else
            diskIo = new List<DiskIoInfo>();
        _prevDiskStat = currDiskStat;

        var diskPartitions = LinuxProcParser.ParseDf(sections[4]);
        var networkLatency = _enableNetworkLatencyProbe
            ? await MeasureNetworkLatencyAsync(ct).ConfigureAwait(false)
            : null;

        return new MonitorSnapshot
        {
            CpuCores = cpuCores,
            Memory = memory,
            NetworkSpeed = netSpeed,
            NetworkLatencyMilliseconds = networkLatency,
            DiskPartitions = diskPartitions,
            DiskIo = diskIo
        };
    }

    private async Task<MonitorSnapshot?> CollectDarwinAsync(CancellationToken ct, long callbackGeneration)
    {
        if (!HasRemoteCommandSource())
            return null;

        var output = await TryRunRemoteCommandAsync(
                DarwinMonitorScript,
                TimeSpan.FromSeconds(15),
                ct,
                "darwin-monitor",
                callbackGeneration)
            .ConfigureAwait(false);
        if (output == null)
            return null;

        return ParseMonitorProtocolOutput(output);
    }

    private async Task<MonitorSnapshot?> CollectWindowsAsync(CancellationToken ct, long callbackGeneration)
    {
        if (!HasRemoteCommandSource())
            return null;

        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(WindowsMonitorScript));
        var cmd = $"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedScript}";
        if (!_hasLoggedWindowsScript)
        {
            _hasLoggedWindowsScript = true;
            DebugLog($"windows monitor script length={WindowsMonitorScript.Length} base64Length={encodedScript.Length}");
        }

        var output = await TryRunRemoteCommandAsync(
                cmd,
                TimeSpan.FromSeconds(10),
                ct,
                "windows-monitor",
                callbackGeneration)
            .ConfigureAwait(false);
        if (output == null)
            return null;

        if (IsPowerShellClixml(output))
        {
            var decoded = DecodePowerShellClixml(output);
            DebugLog($"windows command returned CLIXML on stdout decoded={PreviewForLog(decoded)} raw={PreviewForLog(output)}");
            throw new InvalidOperationException(decoded);
        }

        var snapshot = ParseMonitorProtocolOutput(output);
        snapshot.NetworkLatencyMilliseconds = _enableNetworkLatencyProbe
            ? await MeasureNetworkLatencyAsync(ct).ConfigureAwait(false)
            : null;
        DebugLog($"windows parse result lines={CountNonEmptyLines(output)} cpu={snapshot.CpuCores.Count} memory={snapshot.Memory != null} net={snapshot.NetworkSpeed != null} disks={snapshot.DiskPartitions.Count} diskIo={snapshot.DiskIo.Count}");
        if (snapshot.CpuCores.Count == 0 && snapshot.Memory == null && snapshot.NetworkSpeed == null && snapshot.DiskPartitions.Count == 0 && snapshot.DiskIo.Count == 0)
            DebugLog($"windows parse produced empty snapshot. raw={PreviewForLog(output)}");

        return snapshot;
    }

    private async Task<double?> MeasureNetworkLatencyAsync(CancellationToken ct)
    {
        if (!HasRemoteCommandSource())
            return null;

        const string marker = "__CXSHELL_LATENCY__";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            string? output;
            if (_commandRunner != null)
            {
                output = await _commandRunner($"echo {marker}", TimeSpan.FromSeconds(5), cts.Token)
                    .ConfigureAwait(false);
            }
            else
            {
                output = await Task.Run(() =>
                {
                    if (_sshClient == null || !_sshClient.IsConnected)
                        return null;

                    using var command = _sshClient.CreateCommand($"echo {marker}");
                    command.CommandTimeout = TimeSpan.FromSeconds(5);
                    var result = command.Execute();
                    return command.ExitStatus == 0 ? result : null;
                }, cts.Token).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(output) ||
                !output.Contains(marker, StringComparison.Ordinal))
            {
                DebugLog($"latency probe returned unexpected output={PreviewForLog(output)}");
                return null;
            }

            return Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            DebugLog("latency probe timed out");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog($"latency probe failed exception={ex}");
            return null;
        }
    }

    private bool HasRemoteCommandSource()
    {
        // Keep a disconnected owned client as a valid source so the next
        // sampling cycle can attempt to reconnect it.
        return _commandRunner != null || (_ownsSshClient && _sshClient != null);
    }

    private async Task<bool> EnsureOwnedSshConnectionAsync(CancellationToken ct)
    {
        var client = _sshClient;
        if (!_ownsSshClient || client == null)
            return false;

        if (client.IsConnected)
            return true;

        DebugLog("monitor ssh disconnected; attempting reconnect");
        await Task.Run(() => ConnectWithRetryAsync(client, ct), ct).ConfigureAwait(false);
        var connected = client.IsConnected;
        DebugLog($"monitor ssh reconnect {(connected ? "succeeded" : "did not connect")}");
        return connected;
    }

    private async Task<string?> TryRunRemoteCommandAsync(
        string commandText,
        TimeSpan timeout,
        CancellationToken ct,
        string commandLabel,
        long callbackGeneration)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            if (_commandRunner != null)
            {
                DebugLog($"run command via active terminal label={commandLabel} timeout={timeout.TotalSeconds:F0}s commandLength={commandText.Length}");
                var output = await _commandRunner(commandText, timeout, cts.Token).ConfigureAwait(false);
                DebugLog($"command completed via active terminal label={commandLabel} stdoutLength={output?.Length ?? 0} stdout={PreviewForLog(output)}");
                return output;
            }

            if (!await EnsureOwnedSshConnectionAsync(cts.Token).ConfigureAwait(false))
                throw new InvalidOperationException("The monitor SSH connection is unavailable.");

            var client = _sshClient;
            return await Task.Run(() =>
            {
                if (client == null || !client.IsConnected)
                    throw new InvalidOperationException("The monitor SSH connection closed during reconnect.");

                DebugLog($"run command via monitor ssh label={commandLabel} timeout={timeout.TotalSeconds:F0}s commandLength={commandText.Length}");
                using var command = client.CreateCommand(commandText);
                command.CommandTimeout = timeout;
                var result = command.Execute();
                DebugLog($"command completed via monitor ssh label={commandLabel} exit={command.ExitStatus} stdoutLength={result?.Length ?? 0} stderrLength={command.Error?.Length ?? 0} stdout={PreviewForLog(result)} stderr={PreviewForLog(command.Error)}");
                if (command.ExitStatus != 0)
                {
                    if (string.Equals(commandLabel, "windows-monitor", StringComparison.Ordinal) &&
                        HasWindowsMonitorOutput(result))
                    {
                        DebugLog($"command ignored nonzero exit with usable windows output label={commandLabel} exit={command.ExitStatus}");
                        return result;
                    }

                    if (!string.IsNullOrWhiteSpace(result) && IsPowerShellProgressOnlyClixml(command.Error))
                    {
                        DebugLog($"command ignored progress-only CLIXML stderr label={commandLabel} exit={command.ExitStatus}");
                        return result;
                    }

                    var error = string.IsNullOrWhiteSpace(command.Error)
                        ? $"Remote command exited with code {command.ExitStatus}."
                        : DecodePowerShellClixml(command.Error.Trim());
                    throw new InvalidOperationException(error);
                }

                return result;
            }, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DebugLog($"command cancelled label={commandLabel} timeout={timeout.TotalSeconds:F0}s");
            throw;
        }
        catch (Exception ex)
        {
            var displayMessage = DecodePowerShellClixml(ex.Message);
            DebugLog($"command failed label={commandLabel} displayMessage={PreviewForLog(displayMessage)} exception={ex}");
            ErrorOccurred?.Invoke(
                string.Format(LocalizationService.Shared.Text("Monitor.CommandFailed"), displayMessage),
                callbackGeneration,
                false);
            return null;
        }
    }

    /// <summary>
    /// Shared by the Windows and macOS collectors, which both emit this line protocol.
    /// </summary>
    internal static MonitorSnapshot ParseMonitorProtocolOutput(string output)
    {
        var snapshot = new MonitorSnapshot();
        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var parts = line.Split('|');
            switch (parts[0])
            {
                case "CPU" when parts.Length >= 3:
                    snapshot.CpuCores.Add(new CpuCoreInfo
                    {
                        CoreIndex = ParseIntOrZero(parts[1]),
                        UsagePercent = Math.Clamp(Math.Round(ParseDoubleOrZero(parts[2]), 1), 0, 100)
                    });
                    break;

                case "MEM" when parts.Length >= 3:
                    var totalKb = ParseLongOrZero(parts[1]);
                    var freeKb = ParseLongOrZero(parts[2]);
                    snapshot.Memory = new MemoryInfo
                    {
                        TotalKB = totalKb,
                        FreeKB = freeKb,
                        UsedKB = Math.Max(0, totalKb - freeKb),
                        CachedKB = 0,
                        BuffersKB = 0
                    };
                    break;

                case "NET" when parts.Length >= 3:
                    snapshot.NetworkSpeed = new NetworkSpeed
                    {
                        RxBytesPerSec = Math.Max(0, ParseDoubleOrZero(parts[1])),
                        TxBytesPerSec = Math.Max(0, ParseDoubleOrZero(parts[2])),
                        Timestamp = DateTime.Now
                    };
                    break;

                case "DISK" when parts.Length >= 4:
                    var totalBytes = ParseLongOrZero(parts[2]);
                    var freeBytes = ParseLongOrZero(parts[3]);
                    var usedBytes = Math.Max(0, totalBytes - freeBytes);
                    snapshot.DiskPartitions.Add(new DiskPartitionInfo
                    {
                        Device = parts[1],
                        MountPoint = parts[1],
                        TotalMB = totalBytes / 1024 / 1024,
                        UsedMB = usedBytes / 1024 / 1024,
                        UsagePercent = totalBytes > 0 ? Math.Round(usedBytes * 100.0 / totalBytes, 1) : 0
                    });
                    break;

                case "DIO" when parts.Length >= 4:
                    snapshot.DiskIo.Add(new DiskIoInfo
                    {
                        Device = parts[1],
                        ReadKBPerSec = Math.Max(0, ParseDoubleOrZero(parts[2]) / 1024.0),
                        WriteKBPerSec = Math.Max(0, ParseDoubleOrZero(parts[3]) / 1024.0)
                    });
                    break;
            }
        }

        snapshot.CpuCores = snapshot.CpuCores
            .GroupBy(core => core.CoreIndex)
            .Select(group => group.First())
            .OrderBy(core => core.CoreIndex)
            .ToList();

        if (snapshot.CpuCores.Count > 0 && snapshot.CpuCores.All(core => core.CoreIndex != 0))
        {
            snapshot.CpuCores.Insert(0, new CpuCoreInfo
            {
                CoreIndex = 0,
                UsagePercent = Math.Round(snapshot.CpuCores.Average(core => core.UsagePercent), 1)
            });
        }

        return snapshot;
    }

    private static bool HasWindowsMonitorOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line =>
                line.StartsWith("CPU|", StringComparison.Ordinal) ||
                line.StartsWith("MEM|", StringComparison.Ordinal) ||
                line.StartsWith("NET|", StringComparison.Ordinal) ||
                line.StartsWith("DISK|", StringComparison.Ordinal) ||
                line.StartsWith("DIO|", StringComparison.Ordinal));
    }

    private static int ParseIntOrZero(string text)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static long ParseLongOrZero(string text)
    {
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static double ParseDoubleOrZero(string text)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private void DebugLog(string message)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}";
            lock (_debugLogLock)
            {
                var path = DebugLogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Monitor diagnostics must never affect the UI or collection loop.
        }
    }

    private static string GetDebugLogDirectory()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
                return Path.Combine(localAppData, "CxShell", "Logs");
        }
        catch
        {
        }

        return AppContext.BaseDirectory;
    }

    private static bool IsPowerShellClixml(string? text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               text.IndexOf("<Objs", StringComparison.OrdinalIgnoreCase) >= 0 &&
               text.IndexOf("CLIXML", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string DecodePowerShellClixml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsPowerShellClixml(text))
            return text?.Trim() ?? string.Empty;

        if (IsPowerShellProgressOnlyClixml(text))
            return "PowerShell progress stream.";

        var xmlStart = text.IndexOf("<Objs", StringComparison.OrdinalIgnoreCase);
        if (xmlStart < 0)
            return text.Trim();

        try
        {
            var doc = XDocument.Parse(text[xmlStart..].Trim());
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var streamStrings = doc
                .Descendants(ns + "S")
                .Where(element =>
                {
                    var stream = element.Attribute("S")?.Value;
                    return string.IsNullOrEmpty(stream) ||
                           string.Equals(stream, "Error", StringComparison.OrdinalIgnoreCase);
                })
                .Select(element => DecodePowerShellEscapedString(element.Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct()
                .ToList();

            if (streamStrings.Count > 0)
                return string.Join(Environment.NewLine, streamStrings).Trim();
        }
        catch
        {
        }

        return text.Trim();
    }

    private static bool IsPowerShellProgressOnlyClixml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsPowerShellClixml(text))
            return false;

        var xmlStart = text.IndexOf("<Objs", StringComparison.OrdinalIgnoreCase);
        if (xmlStart < 0)
            return false;

        try
        {
            var doc = XDocument.Parse(text[xmlStart..].Trim());
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var streamObjects = doc
                .Descendants(ns + "Obj")
                .Select(element => element.Attribute("S")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            return streamObjects.Count > 0 &&
                   streamObjects.All(stream => string.Equals(stream, "progress", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static string DecodePowerShellEscapedString(string value)
    {
        return Regex.Replace(value, "_x(?<hex>[0-9A-Fa-f]{4})_", match =>
        {
            var code = int.Parse(match.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return char.ConvertFromUtf32(code);
        });
    }

    private static int CountNonEmptyLines(string text)
    {
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private static string PreviewForLog(string? text, int maxLength = 4000)
    {
        if (string.IsNullOrEmpty(text))
            return "<empty>";

        var normalized = text
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...<truncated>";
    }

    public void Dispose() => Stop();
}
