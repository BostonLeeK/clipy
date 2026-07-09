# Clipy

Windows desktop assistant in the spirit of Clippy. Talks to [Cursor Agent CLI](https://cursor.com/docs/cli/headless) from a floating mascot + chat panel — without opening the full Cursor IDE.

## Features

- Always-on-top floating mascot (orb) and expandable chat panel
- Streaming replies from Cursor Agent (`stream-json`)
- Themes (Clipy Neon, Totoro Forest) with animated mascots
- Attachments: files, folders, clipboard images, screenshots
- Model picker (from `agent models`)
- Global hotkey: `Ctrl+Shift+C` toggle chat, `Ctrl+Shift+S` screenshot
- Tray icon, single-instance, optional autostart

## Requirements

- Windows 10 1809+ / Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or newer with roll-forward)
- [Cursor Agent CLI](https://cursor.com/docs/cli/installation) installed and logged in

## Build & run

```powershell
.\install.ps1
```

Self-contained publish output: `publish/Clipy.exe`

Autostart (optional):

```powershell
.\install.ps1 -RegisterAutostart
```

Start an already-built build:

```powershell
.\start.ps1
```

## Config

Saved at `%APPDATA%\ClipyAssistant\config.json`:

- `workspace` — agent working folder
- `theme_id` — `default` | `kawaii`
- `model_id` — e.g. `auto`, `composer-2.5`
- window position / chat session id

## Project layout

```
Clipy/           WinUI 3 app source
Clipy.sln
install.ps1      publish + launch
start.ps1        launch existing publish
```

## Notes

- The orb uses a separate layered Win32 window for real transparency.