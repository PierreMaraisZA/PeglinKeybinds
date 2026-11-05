# PeglinKeybinds

Rebind Peglin's keyboard and controller controls from inside the game, with button prompts that follow your bindings.

## Features

- **Controls is part of the Options menu.** It's the first row in the main menu's Options. (The pause menu during a run has no Options screen, so change controls before starting a run.) Press **Configure** to open it.
- **Looks like the rest of the game.** The Controls screen is built from Peglin's own Options panel, with the same frame, fonts, buttons and pop-ups.
- **Keyboard and controller side by side.** Every action has a keyboard button and a controller button showing its current bindings, each with its button prompt icon. With no controller plugged in, the controller column shows the game's default pad buttons, greyed out.
- **Plain names, in sections.** Actions are grouped into Gameplay, Menus and Loadout & Encirclepedia, with names like "Aim Left" and "Next Target" in place of the game's internal ones.
- **Works the way Options does.** Move with the mouse, arrow keys, d-pad or stick. Select a binding for Replace, Add (keep the current keys and add another, like the game's three keys for Fire), Default (this binding only) or Unbind. Back and Reset sit at the bottom, where the Options buttons are. Back also closes with the usual back button; Reset has no button shortcut, so you can't hit it by accident, and puts a whole column back to defaults.
- **Ctrl + F5 opens it from the main menu** (it opens Options first). During a run the pause menu has no Options screen, so it does nothing there — change controls before starting a run. Change the key with `OpenControlsKey`.
- **Other mods' keys too.** A Mods section lists the key settings of every installed BepInEx mod, with the mod's own description of what each is for. Changes save straight into that mod's config. Developer and testing keys (debug menus, scene reloads, test tools) are left out.
- **Clash warnings.** A binding turns amber when its key is also used by a mod, or by another game action that doesn't share it in the game's defaults (say Esc on both Pause and Discard Orb), and its pop-up says who else uses it. Keys the game shares on purpose, like Enter for Select and Fire, aren't flagged.
- **Button prompts match your bindings.** The game's on-screen prompts (Accept, Back, tab switching and the rest) show the key or button you've actually bound. If the game has no icon for that key, the mod draws one in the same pixel style.
- **Saved per controller model.** Bindings stick across restarts and apply to any controller of the same model, even after you unplug it and plug it back in.
- **Reset to defaults** for the keyboard, for controllers or for the mouse, from the Reset button.

## Things to know

- **Shared keys stay shared.** Peglin uses some keys for both a menu action and a gameplay action (Enter is both Menu Select and Fire / Confirm). Rebinding one doesn't change the other, so rebind both if you want them to move together.
- **Single keys only.** Combos like Ctrl + Right Arrow can't be recorded. Changing one replaces it with a single key.
- **A few keys are fixed in the game itself** and can't be rebound by any mod: left click (fire, aim and clicking menus), Ctrl+C to copy a seed, and the mouse wheel for stopping scrolling text.
- **Some mods read their keys only at startup.** If a mod key change doesn't take effect, restart the game. Mods that keep their keys outside their main BepInEx config don't show up.

## Installation

**With a mod manager (recommended):** install PeglinKeybinds from Thunderstore with r2modman or the Thunderstore Mod Manager. BepInEx is installed for you.

**Manually:**
1. Install [BepInExPack_Peglin](https://thunderstore.io/c/peglin/p/BepInEx/BepInExPack_Peglin/).
2. Put `PeglinKeybinds.dll` in `Peglin/BepInEx/plugins/`.

## Settings

`BepInEx/config/PeglinKeybinds.cfg` (created the first time the game starts with the mod):

| Setting | Default | What it does |
| --- | --- | --- |
| `ListenSeconds` | `5` | How long the Controls screen waits for a key or button after you choose Change. |
| `OpenControlsKey` | `Ctrl + F5` | Opens Options > Controls from the main menu. `None` turns it off. |

Your bindings are saved in `BepInEx/config/PeglinKeybinds.bindings`. Delete that file while the game is closed to go back to the game's defaults.

## Building

You need the .NET SDK and Peglin with BepInEx installed. `dotnet build -c Release` builds the plugin and copies it into the game's `BepInEx/plugins/PeglinKeybinds` folder. Pass `-p:GamePath="D:\Games\Peglin"` if the game isn't in the default Steam folder. `dotnet test tests/PeglinKeybinds.Tests.csproj` runs the unit tests (they don't need the game).

Debug builds include a UI test harness (`src/DevDump.cs`). It only runs if `BepInEx/config/PeglinKeybinds.dump` exists: it opens Options and the Controls screen, takes screenshots and then quits the game.
