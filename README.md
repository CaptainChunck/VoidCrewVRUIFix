# Void Crew VR UI Fix

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin that makes **Void Crew**'s menus and HUD work properly in VR when you play it with [UUVR](https://github.com/Raicuparta/uuvr), installed through [Rai Pal](https://github.com/Raicuparta/rai-pal).

UUVR turns Void Crew into a VR game, but the game's UI doesn't survive the trip: menus leave ghost copies behind, markers smear across the screen, there's no mouse cursor, and the desktop window goes black. This plugin fixes that. It's a single DLL that sits next to UUVR, and you don't need to change any UUVR settings.

> **Status: early testing.** Main menu and basic gameplay are being tested. Please [report problems](#reporting-a-problem).

<!-- SCREENSHOT: docs/images/hero.png (main menu in the headset with the UI panel and cursor)
![Void Crew main menu in VR with the UI fix](docs/images/hero.png)
-->

## What it fixes

| Problem with UUVR alone | With this plugin |
| --- | --- |
| Closed menus stay on screen as "ghosts"; markers, pings and prompts leave trails | The UI is redrawn cleanly every frame |
| No UI, or see-through and doubled UI in the headset | All menus and the HUD appear on one clear panel in front of you |
| No mouse cursor in VR | A cursor is shown on the panel whenever the game wants one |
| The game window on your monitor is black | The game window shows what the headset sees, with the UI on top, so you can play from it |
| Pings, objective markers and "press E" prompts don't line up with objects | They're placed on the panel so they sit over the objects they belong to |
| Turning on DLSS breaks VR | DLSS and FSR3 are removed from the graphics options while UUVR is installed |
| The intro video doesn't show | The intro video plays on the panel |
| Low frame rate | UUVR's hidden helper cameras, which the game renders as full extra VR views, are turned off |

<!-- SCREENSHOT: docs/images/before-after.png (UUVR alone vs. with the plugin)
![Before and after](docs/images/before-after.png)
-->

## Requirements

- **Void Crew** on PC (Steam).
- A **PC VR headset**. SteamVR is recommended: it gives the sharpest UI (see [Recommended UUVR setting](#recommended-uuvr-setting-optional)).
- **[Rai Pal](https://github.com/Raicuparta/rai-pal)**, a free tool that installs BepInEx and UUVR for you.
- **Keyboard and mouse.** Motion controllers aren't supported yet.

## Installation

### Step 1: Install Rai Pal

Download and run the Rai Pal installer:

**[Download Rai Pal for Windows (RaiPal.exe)](https://github.com/Raicuparta/rai-pal/releases/latest/download/RaiPal.exe)**

Or, if you use winget, run `winget install Raicuparta.RaiPal`.

### Step 2: Find Void Crew in Rai Pal

1. Open Rai Pal. It scans your PC for games, which can take a minute the first time.
2. On the **Games** tab, type `Void Crew` into the **Search...** box.
3. Click **Void Crew** in the list to open its page.

If Void Crew doesn't appear, make sure it's installed through Steam, then refresh Rai Pal. You can also add the game manually: use the **add game** button on the Games tab and select `Void Crew.exe`, or drag `Void Crew.exe` onto the Rai Pal window.

<!-- SCREENSHOT: docs/images/raipal-1-search.png (Games tab with "Void Crew" in the search box)
![Searching for Void Crew in Rai Pal](docs/images/raipal-1-search.png)
-->

### Step 3: Install UUVR from Rai Pal

1. On Void Crew's page, find **UUVR Mono Modern** in the list of mods.
2. Click **Install** next to it. Rai Pal installs **BepInEx** (the mod loader UUVR needs) at the same time.
3. If Rai Pal shows a warning about multiplayer games and anticheat, read it and confirm.
4. When it's done, **UUVR Mono Modern** and **BepInEx** both show a green tick.

> Optional: also install **Config Manager** from the same list. In game it lets you press **F5** to change UUVR's and this plugin's settings.

<!-- SCREENSHOT: docs/images/raipal-2-install-uuvr.png (Void Crew's page with the Install button next to UUVR Mono Modern)
![Installing UUVR Mono Modern](docs/images/raipal-2-install-uuvr.png)
-->

### Step 4: Add this plugin

1. Download **`VoidCrewVRUIFix.dll`** from the [latest release](../../releases/latest).
2. In Rai Pal, on Void Crew's page, find **BepInEx** in the mod list, click its **⋮** button, then **Open Mod Folder**. A folder called `BepInEx` opens.
   - Another way to get there: use the **Folders** menu on Void Crew's page, choose **Installed Mods**, then open `bepinex` and then `BepInEx`.
3. Open the **`plugins`** folder.
4. Drop **`VoidCrewVRUIFix.dll`** into it.

When you're done, the folder looks like this:

```
BepInEx
└── plugins
    ├── uuvr-mono-modern        (UUVR, installed by Rai Pal)
    └── VoidCrewVRUIFix.dll     (this plugin)
```

<!-- SCREENSHOT: docs/images/raipal-3-open-mod-folder.png (the BepInEx row's ⋮ menu with "Open Mod Folder")
![Opening the BepInEx folder from Rai Pal](docs/images/raipal-3-open-mod-folder.png)
-->

<!-- SCREENSHOT: docs/images/plugins-folder.png (File Explorer showing the plugins folder with the DLL in it)
![The plugins folder with VoidCrewVRUIFix.dll](docs/images/plugins-folder.png)
-->

### Step 5: Play

1. Start **SteamVR** (or your headset's VR software).
2. Start **Void Crew** from Steam as normal, or with **Start Game Executable** on its page in Rai Pal.
3. Click on the **game window** so it has keyboard and mouse focus, and play from there. The game window shows what the headset shows, with the UI and cursor on top. SteamVR's "VR View" window is a separate program, so it can't send keyboard and mouse input to the game. If you want to watch VR View as well, set the game to windowed mode in its graphics settings and put the two windows side by side.

## Hotkeys

| Key | What it does |
| --- | --- |
| **F8** | Recentre the UI panel in front of you |
| **F7** | Switch between the SteamVR overlay and the in-game panel |
| **F6** | Flip the UI panel, if it ever appears upside down |
| **F9** | Write a diagnostics report to the log and save a picture of the VR UI (for bug reports) |
| **F3** | Turn VR on or off (this one comes from UUVR) |

You can change all of these in the settings.

## Recommended UUVR setting (optional)

If you use **SteamVR**, set UUVR's **Preferred VR APi** setting to **OpenVr**. The UI is then drawn by SteamVR as an overlay: always sharp, always readable, and unaffected by the game's bloom and depth-of-field effects. With UUVR's default (OpenXr), the plugin draws the UI panel inside the game image instead. That works too, but semi-transparent parts of the UI look slightly darker.

To change it, use either of these:
- In Rai Pal, click **⋮** next to **UUVR Mono Modern**, then **Edit Mod Config**. Set `Preferred VR APi = OpenVr` and save.
- With Config Manager installed, press **F5** in game, open **UUVR**, and change **Preferred VR APi**. Restart the game afterwards.

You don't need to change any other UUVR setting. The plugin turns off UUVR's own UI features automatically while the game runs, and never edits UUVR's files.

## Settings

The plugin creates its settings file the first time the game runs:

`BepInEx\config\com.user.voidcrew.vruifix.cfg`

Edit it with any text editor while the game is closed, or change settings live with **Config Manager** (**F5**). The most useful ones:

| Setting | Default | What it does |
| --- | --- | --- |
| UI Panel → Distance | 1.5 | How far away the UI panel is, in metres |
| UI Panel → Width (degrees) | 70 | How much of your view the panel covers |
| UI Panel → Height Offset | 0 | Moves the panel up or down, in metres |
| UI Panel → Anchor | GameCamera | `GameCamera` keeps the panel in front of the in-game view, so markers line up and you can look around it. `Head` locks it to your head |
| UI Panel → Menu Backdrop Opacity | 0 | Darkens the panel behind menus if text is hard to read against bright scenes (try 0.3) |
| Cursor → Cursor Scale | 1 | Size of the VR cursor |
| General → UI Display Mode | Auto | `Auto`, `SteamVROverlay` or `InGamePanel` |
| Fixes → Desktop View | PanelView | What the game window shows: `PanelView` (what the headset shows, with the UI panel and cursor where they appear in VR), `HeadsetMirror` (Unity's zoomed mirror, no UI) or `UiOnly` |
| Fixes → Desktop View Width (degrees) | 90 | How wide a view the game window shows in `PanelView` |

Every setting has a description in the file.

## Reporting a problem

1. When the problem happens, press **F9** in game.
2. Close the game and collect these files:
   - `BepInEx\LogOutput.log`, which contains the diagnostics report. It's in the same `BepInEx` folder you put the plugin in.
   - `%USERPROFILE%\AppData\LocalLow\Hutlihut Games ApS\Void Crew\VoidCrewVRUIFix\vr-ui-snapshot.png`, a picture of what the VR UI contained when you pressed F9.
   - `%USERPROFILE%\AppData\LocalLow\Hutlihut Games ApS\Void Crew\Player.log`, the game's own log.
3. [Open an issue](../../issues/new) describing what you saw, and attach those files.

## Known limitations

- Keyboard and mouse only. Motion controllers aren't supported yet.
- Multiplayer hasn't been tested much yet.
- Don't enable DLSS or FSR3 from other tools. The plugin hides them in the game's options because they break VR rendering.

## Uninstalling

Delete `VoidCrewVRUIFix.dll` from the `BepInEx\plugins` folder. To remove VR completely, click **Uninstall** next to **UUVR Mono Modern** in Rai Pal, or use **Uninstall all mods** on Void Crew's page.

## Building from source

1. Install Visual Studio 2022 or newer with the **.NET desktop development** workload (.NET Framework 4.8).
2. Install Void Crew, and install UUVR Mono Modern for it with Rai Pal (the build references the game's, BepInEx's and UUVR's DLLs).
3. Open `VoidCrewVRUIFix/VoidCrewVRUIFix.csproj` and set the two paths at the top to match your PC:
   - `VoidCrewDir`: the Void Crew install folder.
   - `BepInExDir`: the Rai Pal BepInEx folder for Void Crew. The `target_assembly` line in `Void Crew\doorstop_config.ini` shows where it is.
4. Build the **Release** configuration. The plugin is `VoidCrewVRUIFix/bin/Release/VoidCrewVRUIFix.dll`.

### How it works

- **UI capture:** while VR is on, after the game's renderer (HDRP) finishes each frame, the plugin asks Unity to draw all screen-space UI (UI Toolkit panels and uGUI canvases) once more into a texture that is cleared every frame.
- **Display:** the texture goes to the headset as a SteamVR overlay (OpenVR) or as a quad drawn by an HDRP custom pass after post-processing (OpenXR).
- **Markers:** `Camera.WorldToViewportPoint` is re-projected for the player camera so markers land in the right place on the panel.
- **UUVR:** the plugin talks to UUVR only through reflection, so a UUVR update can't stop it from loading.

## Credits

- [Raicuparta](https://github.com/Raicuparta) for [UUVR](https://github.com/Raicuparta/uuvr) and [Rai Pal](https://github.com/Raicuparta/rai-pal).
- The [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX) teams.
- Void Crew is made by Hutlihut Games. This is an unofficial fan mod and isn't affiliated with or endorsed by them.
