# Clipy

Windows desktop assistant in the spirit of Clippy. Talks to [Cursor Agent CLI](https://cursor.com/docs/cli/headless) from a floating mascot + chat panel — without opening the full Cursor IDE.

## Features

- Always-on-top floating mascot (orb) and expandable chat panel
- Streaming replies from Cursor Agent (`stream-json`)
- Stop/Cancel while the agent is running
- Local chat history with session list and resume (`%APPDATA%\ClipyAssistant\chats\`)
- Agent / Ask / Plan modes (`--mode`)
- Copy full answers and fenced code blocks
- Quick prompt presets and recent workspace switcher
- Voice input (Windows speech recognition → text)
- Mascot reaction states on the orb (thinking / success / error)
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

### Installer (for releases)

Build a Windows installer + portable zip:

```powershell
.\build-installer.ps1
```

Outputs in `dist/`:

- `Clipy-Setup-<version>-x64.exe` — installer (Start Menu, uninstaller, optional autostart)
- `Clipy-Version-win-x64-portable.zip` — portable build without installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).  
Portable zip only (no Inno):

```powershell
.\build-installer.ps1 -PortableOnly
```

GitHub Release: push a tag `v1.0.0` — workflow `.github/workflows/release.yml` builds and uploads both artifacts.

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
- `agent_mode` — `agent` | `ask` | `plan`
- `local_session_id` / `chat_id` — local history + Cursor resume id
- `recent_workspaces` — last folders (up to 5)
- window position

## Project layout

```
Clipy/           WinUI 3 app source
installer/       Inno Setup script (Clipy.iss)
build-installer.ps1  publish + Setup.exe + portable zip
install.ps1      quick publish + launch
start.ps1        launch existing publish
```

## Notes

- The orb uses a separate layered Win32 window for real transparency.