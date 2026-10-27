# TargetHighlight

A Dalamud plugin for FINAL FANTASY XIV that recolours or removes the brightness flash a character or NPC gets when you target it.

- **Normal**: leave the game's flash alone.
- **No flash**: targeting no longer brightens the model.
- **Custom**: pick your own colour, intensity, fade-out time and fade curve, and optionally hold the colour for as long as the object stays targeted.

This changes the target flash, not the coloured outline highlight. It is purely cosmetic and client-side.

## Install

1. In game, open `/xlsettings` → **Experimental**.
2. Under **Custom Plugin Repositories**, add:
   ```
   https://raw.githubusercontent.com/xRopers/TargetHighlight/main/repo.json
   ```
   then tick **Enabled** and click **Save and Close**.
3. Open `/xlplugins`, search for **TargetHighlight** and install it.

## Commands

| Command | Effect |
| --- | --- |
| `/targethighlight` | Open settings |
| `/targethighlight none` | No flash |
| `/targethighlight default` | Normal game flash |
| `/targethighlight custom` | Custom flash |
| `/targethighlight preview` | Play the custom flash on your current target |

## Building

Requirements:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- XIVLauncher with Dalamud installed and run at least once, so the development files exist in `%AppData%\XIVLauncher\addon\Hooks\dev\`. Set the `DALAMUD_HOME` environment variable to build against Dalamud somewhere else.

```
dotnet build TargetHighlight.sln -c Release
```

The build writes `TargetHighlight/bin/x64/Release/TargetHighlight.dll` with its manifest, plus a packaged `TargetHighlight/bin/x64/Release/TargetHighlight/latest.zip`. To load your own build, add the full path to the DLL under Dev Plugin Locations in `/xlsettings` → **Experimental**.

## Project layout

```
TargetHighlight/
  Plugin.cs                          Entry point: Dalamud services, settings window and /targethighlight
  Configuration.cs                   Saved settings (with FlashMode.cs and FadeCurve.cs)
  Game/GameObjects.cs                Safely gets a target's character model
  Game/SetTintResolver.cs            Finds CharacterBase.SetTint through ClientStructs
  Services/TargetFlashController.cs  The SetTint hook that blocks or replaces the flash
  Services/FlashRecorder.cs          Diagnostics recorder for checking the flash after a game patch
  Utils/ColorConversion.cs           RGB/HSV helpers for the colour fields
  Windows/ConfigWindow.cs            Settings window
images/                              Plugin icon (referenced by the manifest's IconUrl)
scripts/                             Icon generator and the repo.json updater used by the release workflow
repo.json                            Custom plugin repository feed
```

## Releasing

Push a version tag. The [Release workflow](.github/workflows/release.yml) builds the plugin, publishes `latest.zip` as a GitHub release and updates `repo.json` on `main`:

```bash
git tag v0.2.0
git push origin v0.2.0
```

## How it works

When something is targeted, the game sets the model's `CharacterBase.Tint` RGB to 10 and eases it back to 1 over about 40 frames. The shader squares the tint into its colour multiplier. Every tint write goes through `CharacterBase.SetTint`, which this plugin hooks to block the flash or replace it with the custom colour and fade.

`SetTint` is located through [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs), which Dalamud updates for each game patch: the plugin builds the function's byte pattern from ClientStructs' current `Tint` offset and confirms the match against the `CharacterBase` vtable. Small game patches therefore shouldn't need a plugin update; the hook status and how it was found are shown in the Diagnostics tab.

## License

TargetHighlight is licensed under the [GNU Affero General Public License v3.0 or later](LICENSE).
