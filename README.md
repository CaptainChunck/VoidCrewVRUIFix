# Void Crew VR UI Fix

<img src="thunderstore/icon.png" alt="Void Crew VR UI Fix" width="160" align="right">

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin that makes **Void Crew**'s menus and HUD work properly in VR when you play it with [UUVR](https://github.com/Raicuparta/uuvr), installed through [Rai Pal](https://github.com/Raicuparta/rai-pal).

UUVR turns Void Crew into a VR game, but the game's UI doesn't survive the trip: menus leave ghost copies behind, markers smear across the screen, there's no mouse cursor, and the desktop window goes black. This plugin fixes that. It's a single DLL that sits next to UUVR, and you don't need to change any UUVR settings.

> **Status: beta.** Tested with SteamVR on an HTC VIVE Pro 2. Please [report problems](#reporting-a-problem).

**Download:** [`VoidCrewVRUIFix.dll` from the latest release](../../releases/latest) (see [Installation](#installation)). What's new: [CHANGELOG.md](CHANGELOG.md).

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
| The player status HUD sits in a corner of the UI screen, out of view when you look around | It follows your head, at a place you choose |
| The crosshair stays in front of the game camera, and you can only aim with the mouse | The crosshair stays in the middle of your view, and you aim and interact where you look |
| Pings, objective markers and "press E" prompts don't line up with objects | They're placed on the panel so they sit over the objects they belong to |
| Turning on DLSS or FSR3 breaks VR | DLSS works in VR (experimental): it only runs on the player camera, with the fixes it needs. FSR3, which isn't built for VR, is removed from the graphics options |
| The intro video doesn't show | The intro video plays on the panel |
| In a turret, a dark box frames the UI and the turret doesn't shoot where the crosshair is | The dark border is hidden, and the turret turns to wherever you look, with the crosshair in the middle of your view |
| HUD pieces stay stuck on the UI screen, out of view when you look around | Each piece (player HUD, ship status, objectives, turret crosshair, turret ammo and range, weapon stats, FPS counter) can follow your head, placed in the Modify UI window |
| Holding the ping wheel snaps your view and the wheel back, and pings the wrong spot | The wheel stays where you look, and you can look around while it is open: the ping goes to what you look at when you let go |
| Terminal screens follow your head and slide through the terminal and walls | Terminal screens stay on their terminals, like real screens, and the mouse pointer sits on the screen, on the button it points at even when you turn your head |
| Full-screen screens (death screen, spawn screen, fades to black) only cover part of your view | They fill your whole view, with their text on the panel |
| Low frame rate | UUVR's hidden helper cameras, which the game renders as full extra VR views, are turned off |

<!-- SCREENSHOT: docs/images/before-after.png (UUVR alone vs. with the plugin)
![Before and after](docs/images/before-after.png)
-->

## Requirements

- **Void Crew** on PC (Steam).
- A **PC VR headset**. SteamVR is recommended: it gives the sharpest UI (see [UUVR's VR system](#uuvrs-vr-system)).
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
| **F11** | Turn the game picture on your monitor on or off while you play in VR |
| **F3** | Turn VR on or off (this one comes from UUVR) |

You can change all of these in the settings.

## UUVR's VR system

The first time the game starts with the plugin, the plugin switches UUVR's **Preferred VR APi** setting to **OpenVr** (SteamVR) for you. UUVR's own default is OpenXr. With OpenVR the UI is drawn by SteamVR as an overlay: always sharp, always readable, and unaffected by the game's bloom and depth-of-field effects. UUVR picks its VR system just after the plugins load, so this already applies on that first start. If SteamVR isn't installed, UUVR falls back to OpenXR by itself, and the plugin then draws the UI panel inside the game image. That works too, but semi-transparent parts of the UI look slightly darker.

To use OpenXR anyway, set **VR system** in the VR tab (see [Settings](#settings)) and restart the game. The plugin only changes it on its first start, or when you press **RESTORE** on the VR tab.

You don't need to change any other UUVR setting. The plugin turns off UUVR's own UI features automatically while the game runs. The only UUVR settings it changes are the two the VR tab shows: the VR system, and Keep horizon level.

## Settings

The easiest way to change the VR settings is in the game itself: open **Settings** and pick the **VR** tab (next to GRAPHICS). Changes apply straight away, so you can see the result in the headset. Hover the **?** next to a setting to see what it does.

| VR tab setting | What it does |
| --- | --- |
| VR on / off (this session) | Turns VR off or back on without restarting, like UUVR's F3 key. The game always starts in VR |
| VR system (needs restart) | Which VR system UUVR uses. OpenVR (SteamVR) is the default and recommended |
| UI shown as | SteamVR overlay (sharpest, the default), Automatic, or drawn into the game image (also works with OpenXR). With OpenXR the overlay can't be used, so the UI is drawn into the game image |
| **MODIFY UI** (button at the top) | Opens the Modify UI window: a list of every movable piece of the VR UI, a picture of your view showing where each one sits, and sliders for the chosen piece (see below) |
| Aim with your head | While you play, the crosshair stays in the middle of your view and weapons, tools and "press E" interactions aim where you look. Your body still turns with the mouse; menus and terminals stay put. In a turret, the turret turns to wherever you look and the mouse turns your view. On by default |
| Darken world behind menus | Puts a dark layer over the world while a menu or terminal is open, to make text easier to read. Off by default |
| VR mouse pointer size | Size of the mouse pointer on the UI screen (1.8x by default) |
| Keep horizon level (UUVR) | The game only turns you left and right; you look up and down with your head |
| Show game on monitor | Turns the game picture on your monitor on or off while you play in VR. Off leaves the window black |
| Show game on monitor key | The key for that (F11). Click it, then press the key you want |
| Show game on monitor when headset is off | While the monitor picture is off, shows it again whenever you take the headset off, and hides it when you put the headset back on |
| Monitor window shows, view width, view height | What the game window on your monitor shows while you play in VR, how much of the view (100° by default), and where it is aimed. Set the view width to the UI screen size to make the UI fill the window |
| FPS counter in headset | Shows the game's frame rate in your view (Modify UI places it): green at the headset's full refresh rate, yellow at half, red below |
| Monitor V-Sync off in VR | Your monitor's V-Sync and the game's frame limit are turned off while VR is on, so they can't hold back the headset. On by default |
| Allow DLSS (experimental, needs restart) | Keeps DLSS in Graphics > Upscaling. On by default. See [Frame rate in VR](#frame-rate-in-vr) |
| Sharper DLSS (camera jitter) | Gives DLSS the camera jitter it needs for a sharp picture. On by default, only used while DLSS runs |
| Testing: control from SteamVR VR View | Only for testing without a headset (SteamVR's null driver). Leave it off with a real headset |

The defaults are a tested setup: the SteamVR overlay, a UI screen 51° wide and 1.2 m away, a bigger mouse pointer, and every HUD piece locked to your head (see the table below). To go back to them, press **RESTORE** at the bottom of the Settings menu while the **VR** tab is open. It asks first, then puts every VR tab setting, the UI screen and every piece in the Modify UI window back to the defaults. Your other game settings and key bindings stay as they are. On the game's own tabs, RESTORE works as usual and leaves the VR settings alone.

### The Modify UI window

Click **MODIFY UI** at the top of the VR tab. The window lists the pieces you can place:

| Piece | What it is |
| --- | --- |
| UI screen | The floating screen with the menus, markers and the rest of the HUD: size, distance, height, and whether it follows your head |
| Player HUD | Portrait, health, oxygen, temperature, key hints and the class ability |
| Ship status | The ship's hull, power, alloys and biomass |
| Objectives | The mission objectives |
| Turret crosshair | A turret's crosshair and aiming frame, with the warnings next to it. With Aim with your head, the turret aims through it wherever you put it |
| Turret ammo and range | A turret's heat or charge, ammo, range, spread and direction |
| Weapon stats | A turret's weapon stats and mods list |
| FPS counter | The frame-rate counter |

Choose a piece, then use its sliders: **follows your head**, **left / right** and **up / down** (angles of your view), **forwards / backwards** (distance from your eyes; pieces at the same distance sit on the same plane) and **size**. The picture shows where every piece sits in your view; click a box to choose it. While the window is open, every piece shows in the headset (its last picture, or a labelled box if it hasn't been on screen yet): the chosen one solid, the others see-through, so you can line them up live. **RESET THIS PIECE** puts the chosen piece back to its defaults, and **RESTORE** on the VR tab puts them all back.

Where the pieces sit by default (left/right and up/down are angles of your view):

| Piece | Left / right | Up / down | Distance | Size |
| --- | --- | --- | --- | --- |
| UI screen | 51° wide | 0.10 m below the middle | 1.2 m | |
| Player HUD | 8° left | 21° down | 0.6 m | 1.5x |
| Ship status | 24° right | 6° down | 0.5 m | 1.9x |
| Objectives | 17° right | 19° down | 0.6 m | 1.7x |
| Turret crosshair | middle | 1° up | 0.6 m | 2.0x |
| Turret ammo and range | middle | 13° down | 0.6 m | 1.7x |
| Weapon stats | 17° right | 5° up | 0.5 m | 1.9x |
| FPS counter | 17° left | 15° up | 0.6 m | 1.2x |

<!-- SCREENSHOT: docs/images/vr-settings-tab.png (the VR tab in the game's Settings menu)
![The VR tab in the game's Settings menu](docs/images/vr-settings-tab.png)
-->

The plugin also creates a settings file the first time the game runs, which has every option, including a few advanced ones:

`BepInEx\config\com.user.voidcrew.vruifix.cfg`

Edit it with any text editor while the game is closed, or change settings live with **Config Manager** (**F5**). The most useful ones:

| Setting | Default | What it does |
| --- | --- | --- |
| UI Panel → Distance | 1.2 | How far away the UI panel is, in metres |
| UI Panel → Width (degrees) | 51 | How much of your view the panel covers |
| UI Panel → Height Offset | -0.1 | Moves the panel up or down, in metres. Not used in the ship's outside cameras or in turrets aimed with the mouse, so the crosshair lines up with the guns |
| UI Panel → Anchor | GameCamera | `GameCamera` keeps the panel in front of the in-game view, so markers line up and you can look around it. `Head` locks it to your head |
| UI Panel → Moves With Your Head Position | true | SteamVR overlay only. UUVR draws the world with your head's turning but not its movement, so the world moves along when you lean or turn your head. The panel moves along too, so the mouse pointer stays on the button it points at (most noticeable on terminal screens). `false` leaves the panel where it was centred in the room |
| Aiming → Aim With Your Head | true | The crosshair stays in the middle of your view while you play, and weapons, tools and interactions aim where you look. In a turret, the turret turns to where you look |
| Player HUD, Ship Status, Objectives, Turret Crosshair, Turret HUD, Weapon Stats → Follows Your Head | true | Locks that piece to your head; its Left/Right, Up/Down (degrees), Distance (metres) and Size place it (defaults in [The Modify UI window](#the-modify-ui-window)). Turret HUD is the turret's ammo and range readouts. The Modify UI window sets all of these |
| FPS Counter → Left/Right, Up/Down, Distance, Size | -17, 15, 0.6, 1.2 | Where the frame-rate counter sits |
| UI Panel → Menu Backdrop Opacity | 0 | Darkens the world behind menus if text is hard to read against bright scenes (try 0.3). At 1 the world behind menus is completely black |
| Cursor → Cursor Scale | 1.8 | Size of the VR cursor |
| General → UI Display Mode | SteamVROverlay | `SteamVROverlay`, `Auto` or `InGamePanel`. With OpenXR the overlay can't be used, so the in-game panel is used instead |
| Fixes → Desktop View | PanelView | What the game window shows: `PanelView` (what the headset shows, with the UI panel and cursor where they appear in VR), `HeadsetMirror` (Unity's zoomed mirror, no UI) or `UiOnly` |
| Fixes → UI On Terminal Screens | true | While you use a terminal, the mouse pointer and button hints move onto the terminal's screen |
| Fixes → Hide Wheel Backdrop | true | Hides the dark layer the game puts over the screen while the ping wheel is held open, which made the UI screen and HUDs dark boxes in VR |
| Fixes → Hide Turret Screen Border | true | Hides the dark border the game fades in around the screen while you control a turret, which showed as a dark box around the UI in VR |
| Fixes → Keep Terminal Screens In World | true | Keeps a terminal's screen on the terminal while you use it. The game attaches it to the camera, which in VR makes it follow your head |
| Fixes → Desktop View Width (degrees) | 100 | How wide a view the game window shows in `PanelView` |
| Fixes → Disable DLSS | false | `true` removes DLSS from Graphics > Upscaling (restart the game afterwards), if DLSS causes problems in VR |
| Fixes → Desktop View Height (degrees) | 0 | Aims the game window's view up or down in `PanelView`. At 0 it is centred on the UI panel |
| Fixes → Desktop View On | true | Shows the game on your monitor while in VR. `false` leaves the window black |
| Fixes → Desktop View When Headset Is Off | true | Shows the game on the monitor while the headset is taken off, even when Desktop View On is `false` |
| Hotkeys → Desktop View On/Off | F11 | Switches Desktop View On |
| Performance → FPS Counter In Headset | false | Shows the game's frame rate in the top left of your view in the headset |
| Performance → Monitor V-Sync Off In VR | true | Turns off the monitor's V-Sync and the game's frame limit while VR is on, and puts them back afterwards |
| Performance → DLSS Camera Jitter | true | Gives DLSS camera jitter in VR, for a sharper picture |
| Testing → Control From VR View | false | Testing only, for SteamVR's null driver (no headset): while SteamVR's VR View window is focused, your keyboard and mouse still control the game. Typing into text boxes and the scroll wheel aren't passed through |

Every setting has a description in the file.

## Frame rate in VR

SteamVR shows a new picture on every refresh of the headset, for example 90 times a second. When the game can't finish a frame in time, SteamVR runs it at half or a third of that rate and repeats frames, so the frame rate locks to steps like **45** or **30 FPS** on a 90 Hz headset. To get a higher step, the game has to draw each frame faster:

- **Lower SteamVR's render resolution:** in SteamVR's settings, open **Video** and set **Render Resolution** to Custom 100% or lower. Each step down makes every frame cheaper to draw.
- **Use DLSS** (NVIDIA RTX cards): choose **DLSS** under Graphics > Upscaling (**Allow DLSS** in the VR tab is on by default). Its Performance and Ultra Performance modes give the biggest gain.
- **Lower the game's graphics settings**, like shadows and volumetric lighting.
- **Turn off the monitor picture** with **F11** (or **Show game on monitor** in the VR tab) when nobody needs to watch. It saves drawing the game window: a small gain, a few percent at most. With **Show game on monitor when headset is off** on, the picture comes back by itself while the headset is off.

The plugin also does these by itself while you play in VR:

- Turns off your monitor's V-Sync and the game's frame limit, so a 60 Hz monitor can't hold back a 90 Hz headset. They come back when VR is turned off.
- Gives DLSS the camera jitter it needs for a sharp picture (HDRP leaves it out in VR).
- Makes sure HDRP skips the pixels the headset's lenses can't show (the hidden-area mask).

Turn on **FPS counter in headset** in the VR tab (or in the Modify UI window, which also places it) to see the frame rate while you play: green means the game keeps up with the headset, yellow means SteamVR is showing each frame twice, red means less often than that.

Press **F9** in game to check: the **Performance** section of the report shows how long the GPU takes per frame, how often SteamVR repeats each frame, whether the monitor's V-Sync is off, whether the hidden-area mask is used, and whether DLSS is running and at what resolution.

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
- Each eye is drawn in its own pass (multi-pass). Single-pass stereo was tried and doesn't work with Void Crew and UUVR: the two eyes' views didn't turn together.
- FSR3 is hidden in VR: the game's FSR3 has no VR support and would blur the two eyes into each other. DLSS works in VR but is experimental; **Allow DLSS** in the VR tab removes it from the options if it causes problems.

## Uninstalling

Delete `VoidCrewVRUIFix.dll` from the `BepInEx\plugins` folder. To remove VR completely, click **Uninstall** next to **UUVR Mono Modern** in Rai Pal, or use **Uninstall all mods** on Void Crew's page.

## Building from source

1. Install Visual Studio 2022 or newer with the **.NET desktop development** workload (.NET Framework 4.8).
2. Install Void Crew, and install UUVR Mono Modern for it with Rai Pal. The build references the game's, BepInEx's and UUVR's DLLs from your PC; none of them are in this repository.
3. Open `VoidCrewVRUIFix/VoidCrewVRUIFix.csproj` and set the two paths at the top to match your PC:
   - `VoidCrewDir`: the Void Crew install folder.
   - `BepInExDir`: the Rai Pal BepInEx folder for Void Crew. The `target_assembly` line in `Void Crew\doorstop_config.ini` shows where it is.

   Or leave the file alone and pass them on the command line: `msbuild VoidCrewVRUIFix/VoidCrewVRUIFix.csproj -p:Configuration=Release -p:VoidCrewDir="D:\Games\Void Crew" -p:BepInExDir="<Rai Pal's BepInEx folder>"`
4. Build the **Release** configuration. The plugin is `VoidCrewVRUIFix/bin/Release/VoidCrewVRUIFix.dll`.
5. Optional: `powershell -NoProfile -ExecutionPolicy Bypass -File thunderstore\make-package.ps1` makes the Thunderstore package, `dist\Void_Crew_VR_UI_Fix-<version>.zip`, from the Release build and the files in `thunderstore` (`manifest.json`, `README.md`, `icon.png`). `thunderstore\make-icon.ps1` redraws the icon.

### How it works

- **UI capture:** while VR is on, after the game's renderer (HDRP) finishes each frame, the plugin asks Unity to draw all screen-space UI (UI Toolkit panels and uGUI canvases) once more into a texture that is cleared every frame.
- **Display:** the texture goes to the headset as a SteamVR overlay (OpenVR) or as a quad drawn by an HDRP custom pass after post-processing (OpenXR).
- **Markers:** `Camera.WorldToViewportPoint` and the other screen/world conversions are re-projected for the player camera, so markers, pings and terminal clicks land in the right place on the panel.
- **HUD pieces:** each head-locked piece (player HUD, ship status, objectives, turret pieces, weapon stats) is copied out of the captured UI texture and shown as its own picture locked to the head, where the Modify UI window placed it.
- **Head aim:** the game aims along its camera's forward direction, so while it aims (weapons, interactions, pings) the camera is briefly turned to where you look. Turrets get aim input towards the point you look at.
- **SteamVR overlay placement:** UUVR draws the world with the head's turning but not its movement, so the overlay follows the headset's position (not its rotation) and stays lined up with the world.
- **UUVR:** the plugin talks to UUVR only through reflection, so a UUVR update can't stop it from loading. UUVR's own UI handling and its hidden helper cameras are switched off as they start, through Harmony hooks on UUVR's start-up.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## Credits

- [Raicuparta](https://github.com/Raicuparta) for [UUVR](https://github.com/Raicuparta/uuvr) and [Rai Pal](https://github.com/Raicuparta/rai-pal).
- The [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX) teams.
- Void Crew is made by Hutlihut Games. This is an unofficial fan mod and isn't affiliated with or endorsed by them.
