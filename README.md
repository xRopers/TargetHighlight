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

## Releasing

Push a version tag. The [Release workflow](.github/workflows/release.yml) builds the plugin, publishes `latest.zip` as a GitHub release and updates `repo.json` on `main`:

```bash
git tag v0.2.0
git push origin v0.2.0
```

## How it works

When something is targeted, the game sets the model's `CharacterBase.Tint` RGB to 10 and eases it back to 1 over about 40 frames. The shader squares the tint into its colour multiplier. Every tint write goes through `CharacterBase.SetTint`, which this plugin hooks to block the flash or replace it with the custom colour and fade.
