# Built-in X11 Regression Checklist

## Current forwarding design

The built-in X server listens only on the local loopback interface and requires a per-session `MIT-MAGIC-COOKIE-1` cookie. CxShell requests an SSH remote TCP forward for a display in `localhost:10.0` through `localhost:19.0`, sends the binary authority record through the SSH command channel's standard input (the cookie is not embedded in the remote command line), installs a mode-`600` remote authority file, and exports `DISPLAY` and `XAUTHORITY` in the remote shell. The external X server mode remains available.

This is SSH TCP forwarding, not the standard SSH `x11-req` channel. It requires the SSH server to permit remote TCP forwarding. The temporary remote authority file is removed when the shell exits and CxShell also attempts explicit cleanup during disconnect.

External and built-in displays now share one forwarding backend lifecycle for display allocation, port fallback, startup rollback, status reporting, and shutdown. The selected display provider supplies only its local endpoint and optional authorization setup. Automated tests cover configured external displays, remote-port fallback, authorization failure rollback, and idempotent shutdown; they do not replace a real SSH/X11 host test.

Each SSH connection owns a separate X server and random cookie. The cookie is a session access credential, not an application sandbox. Any process running as the same remote Unix user can read or use the session authority file; an authenticated X11 client can inspect or interfere with other clients on that display, including window contents, clipboard, and input (the server implements XTEST and does not implement the X11 SECURITY extension). Use this mode only for trusted GUI applications, and do not treat it as isolation for untrusted remote code or shared Unix accounts. The authority payload is sent over SSH stdin rather than placed in the remote exec command text; the remote file is created under `umask 077`, set to mode `600`, and removed on shell exit/disconnect.

## SSH.NET API decision

The application currently uses SSH.NET 2026.0.0. Its assembly contains an X11 forwarding request on the internal channel type, but that type is not publicly visible and `SshClient` exposes no public X11 request or X11-channel event. The API-surface test verifies this. Calling the internal channel surface through reflection would depend on undocumented implementation details; maintaining a fork solely for this feature is not justified while the existing forwarding path works. Recheck this decision on future SSH.NET upgrades or if a target SSH server disallows remote TCP forwarding.

## Remote application smoke test

On an Ubuntu/Debian test host, install the small test clients if needed:

```sh
sudo apt-get update
sudo apt-get install x11-apps xterm zenity
```

Enable built-in X11 forwarding in the CxShell SSH profile and connect. In the remote shell, verify `DISPLAY` is a forwarded display and `XAUTHORITY` points to a temporary file. Then run:

```sh
xclock
xeyes
xterm
zenity --info --text='CxShell X11 test'
```

Check that each window is fully visible, the clock animates, the eyes follow the pointer, the xterm accepts keyboard input, and the GTK dialog opens and closes. Resize and move windows, open two clients at once, and close them both from the application and from the remote shell.

## Host-side regression matrix

| Scenario | Result |
| --- | --- |
| `xeyes` window is no longer clipped | Confirmed by user on 2026-09-28 |
| `xclock`, `xterm`, and GTK dialog | Requires test on a configured remote host |
| Resize, maximize/restore, close, and multiple windows | Requires test on a configured remote host |
| 100%, 125%, 150%, and 200% display scaling | Requires Windows UI test |
| Multiple monitors and monitor layout changes | Requires Windows UI test |
| Clipboard in both directions | Requires remote GUI test |
| Empty local clipboard clears the previously offered remote text | Automated clipboard-offer test passes; confirm with a remote GUI client |
| Rapid window activation does not let an older clipboard read overwrite newer text | Automated ordering and invalidation tests pass |
| Disconnect/reconnect leaves no listener, window, or authority file | Backend rollback/disposal tests pass; remote-host verification is still required |

The X server protocol and pixel-region logic can be tested locally with:

```powershell
dotnet test CxShell.Tests/CxShell.Tests.csproj --no-restore --filter FullyQualifiedName~X11
```
