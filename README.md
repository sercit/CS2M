# CS2M - Cities: Skylines 2 Multiplayer

[![Discord](https://img.shields.io/discord/508902220943851522.svg)](https://discord.gg/RjACPhd)

## Quick Links

- [Discord Server](https://discord.gg/RjACPhd)
- [Developer Resources](https://cs2.paradoxwikis.com/Modding)

## Introduction

CS2M is an in-development multiplayer / co-op mod for Cities: Skylines 2.

## Build & Deploy

### Prerequisites
- .NET SDK (net472, tested with 9.x)
- Node.js 18+ and npm
- Game installed at path in `Toolchain/Mod.props` (edit if different)

### One-shot build
```pwsh
dotnet build CS2M.sln -c Debug
```
ILRepack merges CS2M.API.dll, CS2M.BaseGame.dll, LiteNetLib, 0Harmony, MessagePack.\* and System.\* deps into a single CS2M.dll. Merged intermediates are auto-deleted from the build output.

The UI bundle (CS2M.mjs, CS2M.css, mod.json) is built into `dist/Mods/CS2M/` via the UI's webpack build (triggered by the C# project).

### Deploy & run
```pwsh
./deploy_and_run.ps1
```
Copies CS2M.dll + UI files + lang/ to the game's Mods folder and launches the game in `-developerMode`. **Do not ship the satellite DLLs** — orphan assemblies with no IMod cause `NullReferenceException` in the mod manager.

## Project layout

| Directory | Purpose |
|---|---|
| `CS2M/` | C# mod entry point (Mod.cs). Minimal OnLoad: registers UISystem, skips BaseGame sync systems & Harmony patches |
| `CS2M.API/` | Shared serialisable command / network types |
| `CS2M.BaseGame/` | Base-game integration: commands, harmony patches (off by default) |
| `CS2M.UI/` | Webpack + TypeScript UI bundle |
| `Toolchain/` | MSBuild props/targets (game path, ILRepack) |

## UI architecture

The mod mounts its main-menu multiplayer launcher via `moduleRegistry.append("GameTopLeft", MainMenuButton)` rather than `moduleRegistry.extend(...)`. This avoids a cohtml/AMD-driver hang (driver 32.0.21033.2001) triggered by extending `transition-group-coordinator.tsx`. SVG icons are inlined as data URLs for the same reason.

## Known issues

- **I18NEverywhere** (PDX #75426) must be disabled in the active playset — it was never updated for game 1.5.7f1 and throws `TypeLoadException` at mod init.
- **AMD driver 32.0.21033.2001** (Sept 2025) hangs ~30s after entering the main menu when cohtml loads SVGs or extends transition-group-coordinator. Fixed in this repo by using append + inline icons.
- The BaseGame sync systems (TimeSystem, MoneySyncSystem, etc.) and Harmony patches are skipped in OnLoad to avoid ECS deadlocks on AMD; they can be re-enabled once a multiplayer server is running.

## License

MIT