# Hornet in Hallownest

Hollow Knight Mod, bringing Hornet as a playable character to the base game.

## Config

The config file can be found as `HornetPlayerMod.GlobalSettings.json` in
- **Windows:** `%USERPROFILE%/AppData/LocalLow/Team Cherry/Hollow Knight`
- **macOS:** `~/Library/Application Support/unity.Team-Cherry.Hollow Knight`
- **Linux:** `~/.config/unity3d/Team Cherry/Hollow Knight` 

**Default config:**
```json
{
  "SilksongPath": null,
  "Controls": {
    "Jump": null,
    "Attack": null,
    "Dash": null,
    "Harpoon": null,
    "Bind": null,
    "Tool": null,
    "Needolin": null,
    "OpenInventory": null,
    // without HK equivalents
    "Taunt": "V",
    "OpenTools": "L",
    "SwitchHero": "Tab"
  }
}
```

Keybindings can be set to letters (`A`–`Z`), digits (`Key0`–`Key9`), function keys (`F1`–`F15`)
and mouse buttons (`LeftButton`, `RightButton`, `Button5`, …), or `null`. 

For the bindings with HK equivalents, `null` means reusing the existing keybinding.

The full list of available keybinds can be found in the [InControl `Key` and `Mouse` enums](https://www.gallantgames.com/incontrol-api/html/namespace_in_control.html).

## Requirements

## Uninstall

Right now, Hornet in Hallownest copies some files into the HK `Managed/` dir.
I'm gonna try to get rid of this before release, but for now in order to fully uninstall the mod, you have to
1. Replace `ScriptingAssemblies.json` with `ScriptingAssemblies.json.hornetbak` (in `hollow_knight_Data`)
2. Remove all assemblies prefixed with `Silksong.` from `Managed/`
2. Remove the following additional assemblies from `Managed/`: `Unity.Addressables.dll`, `Unity.ResourceManager.dll`, `Unity.Profiling.Core.dll`, `Unity.Mathematics.dll`, `Unity.Burst.dll`, `Newtonsoft.Json.UnityConverters.dll`, `TeamCherry.Splines.dll`, `Coffee.SoftMaskForUGUI.dll`.

## Development
