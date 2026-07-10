# Changelog

All notable changes to Clipy are documented in this file.

## [1.1.0] - 2026-07-10

### Added

- Multi-provider agent architecture: **Cursor Agent**, **OpenAI Codex**, and **Claude Code** CLI adapters
- Agent provider picker in Settings with per-provider auth, modes, and models
- `agent_provider`, `codex_path`, and `claude_path` config fields; `agent_provider` stored per chat session
- Theme pack system (`neon`, `totoro`, `grain`) with JSON manifests and per-theme mascots
- Grain Mono theme with redesigned amoeba mascot and animated WebP background
- Ukrainian / English UI localization with runtime language switch
- In-app auto-update check, download, and installer launch
- Native Win32 tray context menu
- App icon generator (`generate-icons.ps1`, `tools/IconGen/`) and proper multi-size `clipy.ico`
- GIF / WebP home-screen background players
- `GdiRenderLock` and `SafeMascotRenderer` for stable mascot rendering across themes
- Delete chat sessions from history with confirmation dialog
- One-sentence summary bubble near the mascot when the panel is collapsed (`OrbSummary`, `OrbBubbleWindow`)
- README showcase assets: app screenshot and animated Grain mascot GIF (`docs/`, `tools/MascotGifGen/`)

### Changed

- `AgentService` refactored into a thin facade over `IAgentProvider` implementations
- Settings auth, status, and footer messages are provider-aware
- Action button hover uses opacity-only styling (fixes white-on-white on Grain Mono)
- Installer and release workflow use generated app icon
- README expanded with feature list, screenshots, and mascot media

### Fixed

- False “update available” prompt when already on the latest version (`1.1.0+build` semver parsing)
- Grain Mono theme crash on switch (concurrent GDI+, blocking WebP load on UI thread)
- Broken tray / app icon (gray circle placeholder)
- Chat freeze when sending from home screen (previous release)
- Orb hover cursor showing wait instead of hand pointer
- Duplicate assistant text in chat and summary bubble after stream completion
- Heavy GIF dithering and edge noise in README mascot animation (static grain overlay, opaque background, global palette without dither)

## [1.0.0] - 2026-07-09

### Added

- Floating mascot orb and expandable chat panel
- Cursor Agent CLI streaming (`stream-json`)
- Stop/Cancel while agent is running
- Local chat history with session list and resume
- Agent / Ask / Plan modes
- Copy answer and fenced code blocks
- Prompt presets and recent workspace switcher
- Voice input via Windows speech recognition
- Mascot reaction states on the orb
- Neon and Totoro Forest themes
- File, folder, clipboard image, and screenshot attachments
- Model picker, global hotkeys, tray icon, single-instance, optional autostart
- Windows installer and portable zip release pipeline
