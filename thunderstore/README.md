# Void Crew VR UI Fix

Makes **Void Crew**'s menus and HUD work properly in VR when you play it with [UUVR](https://github.com/Raicuparta/uuvr), installed through [Rai Pal](https://github.com/Raicuparta/rai-pal).

> **Needs UUVR.** UUVR isn't on Thunderstore, and mod managers (r2modman, Thunderstore Mod Manager, Gale) can't install it. Install UUVR with Rai Pal, then put this mod into the BepInEx folder Rai Pal made, as shown below. Keyboard and mouse only; motion controllers aren't supported yet.

## What it does

- **One sharp UI screen.** All menus and the HUD on one clear screen in front of you, drawn by SteamVR as an overlay, with a mouse cursor. No more ghost copies, smeared markers or doubled UI.
- **Your HUD, where you want it.** The player status, ship status, objectives, turret crosshair, turret ammo and range, weapon stats and an FPS counter can each follow your head. Place them in the **Modify UI** window, which shows a picture of your view.
- **Aim with your head.** Weapons, tools, "press E" interactions and pings go where you look, while your body still turns with the mouse. Turrets turn to wherever you look.
- **Terminals that work.** Terminal screens stay on their terminals, and the mouse pointer sits on the screen, on the button it points at.
- **Markers that line up.** Pings, objective markers and prompts sit over the objects they belong to.
- **A VR tab** in the game's Settings menu. Every setting changes live in the headset, and RESTORE puts the tested defaults back.
- **Your monitor shows the game** while you play in VR, with the UI, so others can watch. F11 turns it off.
- **Faster.** UUVR's hidden helper cameras are turned off, and DLSS works in VR (experimental).

It only changes things on your own PC; other players don't need it.

## Installation

1. Install **[Rai Pal](https://github.com/Raicuparta/rai-pal/releases/latest/download/RaiPal.exe)** and open it. On the **Games** tab, search for `Void Crew` and click it.
2. Click **Install** next to **UUVR Mono Modern**. Rai Pal installs **BepInEx** with it.
3. On this page, click **Manual Download** and open the zip.
4. In Rai Pal, on Void Crew's page, click the **⋮** next to **BepInEx**, then **Open Mod Folder**.
5. Copy **`VoidCrewVRUIFix.dll`** from the zip's `plugins` folder into the **`plugins`** folder there, next to `uuvr-mono-modern`.
6. Start **SteamVR**, then **Void Crew**. Click on the game window so it has the keyboard and mouse.

The first time the game starts with the mod, it switches UUVR to OpenVR (SteamVR) for the sharp overlay UI. Everything else works out of the box.

## Settings

Open **Settings** in the game and pick the **VR** tab (next to GRAPHICS). Click **MODIFY UI** at the top to move, resize and lock to your head every piece of the HUD. **RESTORE** at the bottom of the Settings menu, pressed on the VR tab, puts the defaults back.

## Hotkeys

| Key | What it does |
| --- | --- |
| F8 | Recentre the UI in front of you |
| F7 | Switch between the SteamVR overlay and the in-game panel |
| F6 | Flip the UI if it is ever upside down |
| F9 | Write a diagnostics report (for bug reports) |
| F11 | Turn the game picture on your monitor on or off |
| F3 | Turn VR on or off (from UUVR) |

## Problems and source code

The full guide, the source code and bug reports are on GitHub: https://github.com/CaptainChunck/VoidCrewVRUIFix

When something goes wrong, press **F9** in game, then attach `BepInEx\LogOutput.log` to your report.

## Credits

[UUVR](https://github.com/Raicuparta/uuvr) and [Rai Pal](https://github.com/Raicuparta/rai-pal) by Raicuparta, [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX). Void Crew is made by Hutlihut Games; this mod is a fan project and isn't affiliated with them.
