using System.Linq;
using CxShell.Services;

namespace CxShell.Tests;

/// <summary>
/// The macOS collector emits the same line protocol as the Windows one, so these
/// assertions run the parser over output captured from a real Darwin host: the
/// page size lives in vm_stat's header, and APFS makes df report a dozen fake disks.
/// </summary>
public sealed class DarwinMonitorCollectorTests
{
    private const string CapturedDarwinOutput = """
CPU|0|28.8
MEM|16777216|4060512
DISK|/|999995129856|591401226240
DISK|/System/Volumes/Data|999995129856|591401226240
DISK|/Volumes/Macintosh|245107195904|205498572800
DISK|/Volumes/Data|245107195904|205498572800
NET|44271|8087
""";

    [Fact]
    public void CapturedOutput_PopulatesEveryPanelTheViewReads()
    {
        var snapshot = ServerMonitorService.ParseMonitorProtocolOutput(CapturedDarwinOutput);

        var cpu = Assert.Single(snapshot.CpuCores);
        Assert.Equal(0, cpu.CoreIndex);
        Assert.Equal(28.8, cpu.UsagePercent, 1);

        Assert.NotNull(snapshot.Memory);
        Assert.Equal(16777216, snapshot.Memory.TotalKB);
        Assert.Equal(4060512, snapshot.Memory.FreeKB);
        Assert.Equal(16777216 - 4060512, snapshot.Memory.UsedKB);

        Assert.NotNull(snapshot.NetworkSpeed);
        Assert.Equal(44271, snapshot.NetworkSpeed.RxBytesPerSec, 1);
        Assert.Equal(8087, snapshot.NetworkSpeed.TxBytesPerSec, 1);

        Assert.Equal(
            new[] { "/", "/System/Volumes/Data", "/Volumes/Macintosh", "/Volumes/Data" },
            snapshot.DiskPartitions.Select(partition => partition.MountPoint));
    }

    [Fact]
    public void DiskLines_ConvertBytesIntoTheMegabytesTheModelStores()
    {
        var snapshot = ServerMonitorService.ParseMonitorProtocolOutput(CapturedDarwinOutput);

        var root = snapshot.DiskPartitions[0];
        Assert.Equal(999995129856 / 1024 / 1024, root.TotalMB);
        Assert.Equal((999995129856 - 591401226240) / 1024 / 1024, root.UsedMB);
        Assert.Equal(40.9, root.UsagePercent, 1);
    }

    [Fact]
    public void MissingLines_LeaveThoseSectionsEmptyRatherThanThrowing()
    {
        var snapshot = ServerMonitorService.ParseMonitorProtocolOutput("MEM|1024|512\n");

        Assert.Equal(1024, snapshot.Memory!.TotalKB);
        Assert.Empty(snapshot.CpuCores);
        Assert.Empty(snapshot.DiskPartitions);
        Assert.Null(snapshot.NetworkSpeed);
    }

    [Fact]
    public void UnknownAndMalformedLines_DoNotDerailTheRestOfTheSample()
    {
        var snapshot = ServerMonitorService.ParseMonitorProtocolOutput(
            "sh: awk: command not found\nCPU|0|not-a-number\nMEM|1024\nNET|7|9\n");

        Assert.Equal(7, snapshot.NetworkSpeed!.RxBytesPerSec, 1);

        // A line with the right shape but an unreadable number is a pre-existing
        // tolerance in this parser: it lands as 0 rather than being dropped. Asserted
        // so a change here is a deliberate one.
        Assert.Equal(0, Assert.Single(snapshot.CpuCores).UsagePercent);

        // MEM|1024 is short of the two values it needs, so memory stays absent.
        Assert.Null(snapshot.Memory);
        Assert.Empty(snapshot.DiskPartitions);
    }
}
