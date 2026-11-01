# BetterEnemyAI

Source for the [BetterEnemyAI](https://thunderstore.io/c/valheim/p/aetuss/BetterEnemyAI/) Valheim mod: enemies stop circling around you after attacking, flyers circle more softly, chases end sooner, and the server's settings are synced to everyone.

The player-facing description lives in [package/README.md](package/README.md).

## Layout

- `src/` — plugin code (BepInEx + Harmony)
- `package/` — Thunderstore files: `manifest.json`, `README.md`, `icon.png`
- `build.ps1` — builds the DLL and the Thunderstore zip

## Building

Needs Valheim and a BepInEx install (for `BepInEx.dll` and `0Harmony.dll`). No .NET SDK is required; the script uses the C# compiler that ships with Windows.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Pass `-Managed` and `-BepInExCore` if your game or BepInEx folders are elsewhere. The output is `bin/BetterEnemyAI.dll` and `dist/BetterEnemyAI-<version>.zip`, ready to upload to Thunderstore.

## Releasing

Bump the version in both `src/Plugin.cs` (`Version`) and `package/manifest.json` (`version_number`), build, and upload the zip from `dist/`.
