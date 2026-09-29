# Windows Virtual Desktop Helper (local build)

A tray app for Windows 11 virtual desktops: shows the current desktop in the tray, switches instantly, and lets you manage desktops and windows from the tray menu or with hotkeys.

## Build

Close the app first (the running exe is locked), then:

```
powershell -ExecutionPolicy Bypass -File Scripts\build-local.ps1
```

This builds `dist\WindowsVirtualDesktopHelper.exe` with just the .NET SDK (no Visual Studio needed).

## Features

- Desktop number (and optionally the name initial) in the tray, with a tooltip like "Work - desktop 2 of 4"
- Instant updates via Windows' own desktop change notifications (Windows 11 24H2/25H2)
- Tray menu (right-click the number):
  - all desktops, click to switch
  - new / rename / close desktop
  - move the last used window to another desktop, or show it on all desktops
  - **Pin windows to all desktops**: a checklist of every open window; click to pin/unpin, the menu stays open
  - options: wrap around, color per desktop, switch along when moving a window, open config/log
- Hotkeys (defaults, change them in the config):
  - `Ctrl + Shift + Win + Right/Left` move the active window to the next/previous desktop
  - `Ctrl + Shift + Win + P` pin/unpin the active window on all desktops
  - `Alt + 1..9` jump to a desktop, `Alt + ~` back to the previous desktop (off by default, enable in the config)
- Optional switch overlay and permanent status overlay
- A notification when a hotkey can't be registered because another app uses it

## Config

`%APPDATA%\WindowsVirtualDesktopHelper\WindowsVirtualDesktopHelper.exe.config` (tray menu: Options > Open config folder). Changes apply immediately. All options: tray menu > About > Show Config, or [Documentation/Settings.md](Documentation/Settings.md), [Hotkeys](Documentation/Hotkeys.md), [Actions](Documentation/Actions.md).

The log is written to `WindowsVirtualDesktopHelper.log` in the same folder.

## License

Based on Windows Virtual Desktop Helper, licensed under the GNU GPL v3 (see [LICENSE.md](LICENSE.md)). The virtual desktop API code is MIT licensed (see [Source/VirtualDesktopAPI/LICENSE.md](Source/VirtualDesktopAPI/LICENSE.md)).
