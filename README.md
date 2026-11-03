# Deafen

Deafen automatically mutes selected Windows applications while their windows are minimized or in the background. When a tracked window becomes active again, Deafen restores the mute state that its audio session had before Deafen changed it.

## How it works

- Tracks applications by executable name.
- Detects minimized windows and foreground-window changes, including borderless applications.
- Applies mute state per Windows audio session without changing volume.
- Remembers and restores each audio session's original mute state.
- Uses Windows accessibility events for prompt updates and a periodic check to discover new processes and audio sessions.
- Does not mute a tracked process when it has no detectable application window.

## Manage tracked applications

Add an application from the **Running apps** list, or use **Add by name** to enter an executable name such as `notepad.exe`. The running-app list is refreshed when opened and groups multiple windows belonging to the same executable. Select **Delete** beside an entry to stop tracking it.

The tracked-process list is empty on a fresh installation. Deafen creates a `Deafen.json` settings file beside the executable and saves changes as they are made. Process names can be entered with or without the `.exe` extension; use a name, not a full file path.

Example configuration:

```json
{
  "Processes": [],
  "MinimizeToTray": true
}
```

## Windows startup and system tray

- **Start with Windows** launches Deafen when the current user signs in.
- **Minimize to tray on close** keeps Deafen running when its window is closed.
- Select **Open** in the tray menu to show the window, or **Exit** to stop monitoring and restore audio states.
- Only one Deafen instance runs at a time. Launching it again brings the existing window forward.

## Build from source

Deafen is a Windows Forms application written in C# and uses the Windows Core Audio APIs. No third-party packages are required. Build it from PowerShell in the project directory with the .NET Framework C# compiler:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /warn:4 /target:winexe /optimize+ /win32icon:Deafen.ico /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /out:Deafen.exe AudioSessionController.cs DeafenApp.cs
```
