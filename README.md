# CS2M - Cities: Skylines 2 Multiplayer

[![Discord](https://img.shields.io/discord/508902220943851522.svg)](https://discord.gg/RjACPhd)

## Quick Links

- [Discord Server](https://discord.gg/RjACPhd)
- [Developer Resources](https://cs2.paradoxwikis.com/Modding)
- [Patreon](https://www.patreon.com/CSM_MultiplayerMod)

## Introduction

CS2M is an in-development multiplayer / co-op mod for Cities: Skylines 2. This mod aims to provide a simple client-server experience where users can play and build together in a single game.

Feel free to join the development Discord server [here](https://discord.gg/RjACPhd).

## Developer Resources

### Mod Dependencies

Other Cities Skylines 2 mods that are required for CS2M to function properly:

- [I18n EveryWhere](https://mods.paradoxplaza.com/mods/75426/Windows)

### Build

Requires:
- .NET SDK targeting `net472` (the solution is tested with the 9.x SDK).
- Node.js 18+ and npm for the UI sub-project.
- A copy of Cities: Skylines II with its `Cities2_Data/Managed` directory intact; the `Toolchain/Mod.props` file points at the game install (edit it if your path differs).

Build the C# solution and the UI bundle in one shot:
```pwsh
dotnet build CS2M.sln -c Debug
```
This runs ILRepack to merge `CS2M.API.dll`, `CS2M.BaseGame.dll`, `LiteNetLib`, `0Harmony`, `MessagePack.*` and the `System.*` dependencies into a single `CS2M.dll`. The merged intermediate copies are then deleted from the build output so they can never be shipped in the mod folder by accident.

The UI bundle (`CS2M.mjs`, `CS2M.css`, `mod.json`) is built into `dist/Mods/CS2M/` as a side effect of the `CS2M` project.

### Deploy & run

```pwsh
./deploy_and_run.ps1
```

This copies:
- `CS2M.dll` — the only C# assembly in the mod folder. It contains the `Mod` class (which implements `Game.Modding.IMod`) plus everything that used to ship as separate `CS2M.API.dll` / `CS2M.BaseGame.dll` before the ILRepack step.
- `CS2M.mjs`, `CS2M.css` and `mod.json` from `dist/Mods/CS2M/`.
- The `lang/` directory of localised strings.

It then launches the game in `-developerMode` so the content manager and mod loader are available without the Paradox launcher.

**Do not copy the satellite DLLs** (LiteNetLib, MessagePack.*, 0Harmony, System.*) or the intermediate `CS2M.API.dll`/`CS2M.BaseGame.dll` into the mod folder. The game's mod manager treats every DLL in the folder as a potential mod; orphan DLLs with no `IMod` cause a `NullReferenceException` in `ModManager+ModInfo.get_assemblyFullName` and the mod silently fails to initialise its UI bindings.

### Project layout

- `CS2M/`         — main C# project. `Mod.cs` is the game entry point.
- `CS2M.API/`     — shared serialisable command / network types referenced by all other projects.
- `CS2M.BaseGame/`— base-game integration: commands, systems, Harmony patches.
- `CS2M.UI/`      — webpack + TypeScript UI bundle (`mod.json`, screens, bindings).
- `Toolchain/`    — shared MSBuild props/targets (game path, ILRepack config).

## Contributors
A full list of contributors can be found [here](https://github.com/CitiesSkylinesMultiplayer/CS2M/graphs/contributors).

## License

This mod and its source code is licensed under the MIT license.