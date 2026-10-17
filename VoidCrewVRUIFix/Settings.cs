using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace VoidCrewVRUIFix
{
    internal enum UiDisplayMode
    {
        Auto,
        SteamVROverlay,
        InGamePanel
    }

    internal enum PanelAnchor
    {
        GameCamera,
        Head
    }

    internal enum DesktopViewMode
    {
        PanelView,
        HeadsetMirror,
        UiOnly
    }

    /// <summary>
    /// All user-facing options. The file is created automatically at
    /// BepInEx/config/com.user.voidcrew.vruifix.cfg the first time the game runs with the plugin.
    /// The defaults are what players should use; nothing needs editing for a normal install.
    /// </summary>
    internal static class Settings
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<UiDisplayMode> DisplayMode;

        public static ConfigEntry<float> PanelDistance;
        public static ConfigEntry<float> PanelWidthDegrees;
        public static ConfigEntry<float> PanelHeightOffset;
        public static ConfigEntry<PanelAnchor> Anchor;
        public static ConfigEntry<float> MenuBackdropOpacity;
        public static ConfigEntry<bool> FlipVertically;

        public static ConfigEntry<bool> AimWithHead;
        public static ConfigEntry<bool> HudFollowsHead;
        public static ConfigEntry<float> HudLeftRight;
        public static ConfigEntry<float> HudUpDown;
        public static ConfigEntry<float> HudDistance;
        public static ConfigEntry<float> HudSize;

        public static ConfigEntry<bool> ShowVrCursor;
        public static ConfigEntry<float> CursorScale;
        public static ConfigEntry<bool> HideHardwareCursor;

        public static ConfigEntry<bool> AlignMarkersToPanel;
        public static ConfigEntry<bool> MoveCameraCanvasesToPanel;
        public static ConfigEntry<bool> FixIntroVideo;
        public static ConfigEntry<bool> KeepTerminalScreensInWorld;
        public static ConfigEntry<bool> SnapUiToTerminalScreens;
        public static ConfigEntry<bool> DisableDlss;
        public static ConfigEntry<bool> DisableFsr3;
        public static ConfigEntry<bool> DisableUuvrUi;
        public static ConfigEntry<bool> DisableUuvrHelperCameras;
        public static ConfigEntry<DesktopViewMode> DesktopView;
        public static ConfigEntry<float> DesktopViewWidth;
        public static ConfigEntry<float> DesktopViewHeight;
        public static ConfigEntry<bool> DesktopViewOn;
        public static ConfigEntry<bool> DesktopViewWhenHeadsetOff;
        public static ConfigEntry<bool> ClearDesktopEachFrame;

        public static ConfigEntry<bool> MonitorVSyncOffInVr;
        public static ConfigEntry<bool> DlssCameraJitter;
        public static ConfigEntry<bool> ShowFpsCounter;

        public static ConfigEntry<Key> KeyFlip;
        public static ConfigEntry<Key> KeyDisplayMode;
        public static ConfigEntry<Key> KeyRecenter;
        public static ConfigEntry<Key> KeyDiagnostics;
        public static ConfigEntry<Key> KeyDesktopView;

        public static ConfigEntry<bool> ControlFromVrView;

        public static ConfigEntry<bool> SubmitOverlayOnRenderThread;
        public static ConfigEntry<bool> VerboseLogging;
        private static ConfigEntry<int> _settingsVersion;

        private const int CurrentSettingsVersion = 4;

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                "Master switch for the whole plugin.");
            DisplayMode = config.Bind("General", "UI Display Mode", UiDisplayMode.Auto,
                "How the game UI is shown in the headset.\n" +
                "Auto: SteamVR overlay when UUVR uses OpenVR, otherwise the in-game panel.\n" +
                "SteamVROverlay: sharpest result, drawn by SteamVR on top of the game (OpenVR only).\n" +
                "InGamePanel: drawn into the game image (works with OpenVR and OpenXR).");

            PanelDistance = config.Bind("UI Panel", "Distance", 1.5f,
                new ConfigDescription("Distance of the UI panel from your eyes, in metres.",
                    new AcceptableValueRange<float>(0.5f, 5f)));
            PanelWidthDegrees = config.Bind("UI Panel", "Width (degrees)", 70f,
                new ConfigDescription("How much of your view the UI panel covers horizontally. " +
                    "World markers and pings are re-projected so they still line up with objects.",
                    new AcceptableValueRange<float>(30f, 120f)));
            PanelHeightOffset = config.Bind("UI Panel", "Height Offset", 0f,
                new ConfigDescription("Moves the panel up (positive) or down (negative), in metres.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            Anchor = config.Bind("UI Panel", "Anchor", PanelAnchor.GameCamera,
                "GameCamera: the panel stays in front of the in-game view direction, so you can look around it and markers line up with the world.\n" +
                "Head: the panel is locked to your head and always stays in front of your eyes.");
            MenuBackdropOpacity = config.Bind("UI Panel", "Menu Backdrop Opacity", 0f,
                new ConfigDescription("Darkens the game world behind menus and terminals while one is open (shown as 'Darken World Behind Menus' in the VR tab). " +
                    "0 = off. Try 0.3 if menu text is hard to read against bright scenes. At 1 the world behind menus is completely black; the VR tab stops at 0.8.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FlipVertically = config.Bind("UI Panel", "Flip Vertically", false,
                "Turn on if the UI appears upside down in the headset. Can also be toggled in game with the Flip UI hotkey.");

            AimWithHead = config.Bind("Aiming", "Aim With Your Head", true,
                "While you play (no menu or terminal open), the UI screen follows your head so the crosshair stays in the middle of your view, " +
                "and weapons, tools and 'press E' interactions aim where you look. Your body still turns with the mouse. " +
                "Off: you aim with the mouse only and the crosshair stays in front of the game camera.");
            HudFollowsHead = config.Bind("Player HUD", "Follows Your Head", true,
                "Shows the player status HUD (portrait, health, oxygen, temperature) locked to your head, so it stays in view wherever you look. " +
                "Off: it stays on the UI screen with the rest of the HUD.");
            HudLeftRight = config.Bind("Player HUD", "Left/Right (degrees)", -20f,
                new ConfigDescription("Where the HUD sits in your view, left (negative) or right (positive).", new AcceptableValueRange<float>(-45f, 45f)));
            HudUpDown = config.Bind("Player HUD", "Up/Down (degrees)", -18f,
                new ConfigDescription("Where the HUD sits in your view, down (negative) or up (positive).", new AcceptableValueRange<float>(-45f, 45f)));
            HudDistance = config.Bind("Player HUD", "Distance", 1f,
                new ConfigDescription("How far in front of your eyes the HUD floats, in metres. It is always kept nearer than the UI screen.",
                    new AcceptableValueRange<float>(0.3f, 3f)));
            HudSize = config.Bind("Player HUD", "Size", 1f,
                new ConfigDescription("Size of the HUD: 1 is as big as it looks on the UI screen.", new AcceptableValueRange<float>(0.5f, 2f)));

            ShowVrCursor = config.Bind("Cursor", "Show VR Cursor", true,
                "Draws the mouse cursor on the UI panel whenever the game shows a cursor (menus, terminals).");
            CursorScale = config.Bind("Cursor", "Cursor Scale", 1f,
                new ConfigDescription("Size multiplier for the VR cursor.", new AcceptableValueRange<float>(0.5f, 4f)));
            HideHardwareCursor = config.Bind("Cursor", "Hide Windows Cursor", true,
                "Hides the normal Windows cursor while in VR so you don't see two cursors on the desktop window.");

            AlignMarkersToPanel = config.Bind("Fixes", "Align Markers To Panel", true,
                "Re-projects pings, objective markers and interaction prompts so they line up with world objects on the VR UI panel.");
            MoveCameraCanvasesToPanel = config.Bind("Fixes", "Move Camera Canvases To Panel", true,
                "Moves HUD canvases that the game renders in front of the camera onto the VR UI panel, instead of a flat sheet stuck to your face.");
            FixIntroVideo = config.Bind("Fixes", "Fix Intro Video", true,
                "Shows the start-up intro video on the VR UI panel. The game normally draws it on the camera near plane, which breaks in VR.");
            KeepTerminalScreensInWorld = config.Bind("Fixes", "Keep Terminal Screens In World", true,
                "Keeps a terminal's screen on the terminal while you use it. The game attaches it to the camera, which in VR makes it follow your head " +
                "and slide through the terminal and walls.");
            SnapUiToTerminalScreens = config.Bind("Fixes", "UI On Terminal Screens", true,
                "While you use a terminal, the VR UI panel (mouse pointer and button hints) moves onto the terminal's screen, at its distance and size, " +
                "so the pointer sits on the screen itself. Only when the UI panel stays in front of the game camera (not when it follows your head).");
            DisableDlss = config.Bind("Fixes", "Disable DLSS", true,
                "Removes DLSS from the graphics options while UUVR is installed. Set to false to try DLSS in VR (experimental): " +
                "restart the game, then pick DLSS under Graphics > Upscaling. DLSS then only runs on the player camera.");
            DisableFsr3 = config.Bind("Fixes", "Disable FSR3", true,
                "Removes FSR3 from the graphics options while UUVR is installed. FSR3 is not built for VR rendering.");
            DisableUuvrUi = config.Bind("Fixes", "Disable UUVR UI", true,
                "Turns off UUVR's own UI handling at runtime (its config file is not changed). Required: UUVR's UI modes conflict with this plugin in HDRP games like Void Crew.");
            DisableUuvrHelperCameras = config.Bind("Fixes", "Disable UUVR Helper Cameras", true,
                "Turns off the hidden helper cameras UUVR creates for head tracking. HDRP renders each one as a full extra VR camera, which costs a lot of FPS. " +
                "Only applied when UUVR's Camera Tracking Mode is RelativeTransform (the default).");
            DesktopView = config.Bind("Fixes", "Desktop View", DesktopViewMode.PanelView,
                "What the game window on your monitor shows while in VR.\n" +
                "PanelView: what the headset shows (right eye), with the UI panel and cursor drawn where they appear in VR. You can play from it.\n" +
                "HeadsetMirror: Unity's own mirror of the right eye (zoomed in, no UI).\n" +
                "UiOnly: just the UI and cursor, full size, on black.");
            DesktopViewWidth = config.Bind("Fixes", "Desktop View Width (degrees)", 90f,
                new ConfigDescription("How wide a view the game window shows in PanelView mode. Limited to what the headset renders.",
                    new AcceptableValueRange<float>(30f, 170f)));
            DesktopViewHeight = config.Bind("Fixes", "Desktop View Height (degrees)", 0f,
                new ConfigDescription("Aims the game window's view up (positive) or down (negative) in PanelView mode. At 0 the view is centred on the UI panel. " +
                    "Aiming up moves the UI down the window.",
                    new AcceptableValueRange<float>(-30f, 30f)));
            DesktopViewOn = config.Bind("Fixes", "Desktop View On", true,
                "Shows the game in the game window on your monitor while in VR. Off leaves the window black and saves a little graphics work. " +
                "The Desktop View On/Off hotkey switches it.");
            DesktopViewWhenHeadsetOff = config.Bind("Fixes", "Desktop View When Headset Is Off", true,
                "While Desktop View On is off, shows the game on the monitor again whenever the headset is taken off (using its proximity sensor), " +
                "and hides it again when the headset is put back on.");
            ClearDesktopEachFrame = config.Bind("Fixes", "Clear Desktop Each Frame", true,
                "Clears the game window every frame. Stops old UI images being left behind as 'ghosts' or trails on the desktop.");

            MonitorVSyncOffInVr = config.Bind("Performance", "Monitor V-Sync Off In VR", true,
                "Turns off the monitor's V-Sync and the game's frame limit while VR is on, so they can't hold back the headset's frame rate " +
                "(SteamVR paces the frames). The game's own settings come back when VR is turned off.");
            ShowFpsCounter = config.Bind("Performance", "FPS Counter In Headset", false,
                "Shows the game's frame rate in the top left of your view in the headset. Green: the headset's full refresh rate. " +
                "Yellow: half of it or more. Red: lower.");
            DlssCameraJitter = config.Bind("Performance", "DLSS Camera Jitter", true,
                "Gives DLSS the tiny per-frame camera jitter it needs to rebuild fine detail. HDRP turns jitter off in VR, which makes DLSS blurry. " +
                "Only used while DLSS is running. Turn off if edges shimmer.");

            KeyFlip = config.Bind("Hotkeys", "Flip UI", Key.F6,
                "Flips the VR UI panel upside down (use if it appears inverted). Set to None to disable.");
            KeyDisplayMode = config.Bind("Hotkeys", "Switch Display Mode", Key.F7,
                "Switches between the SteamVR overlay and the in-game panel.");
            KeyRecenter = config.Bind("Hotkeys", "Recenter UI", Key.F8,
                "Moves the UI panel back in front of you.");
            KeyDiagnostics = config.Bind("Hotkeys", "Write Diagnostics", Key.F9,
                "Writes a detailed diagnostics report to the BepInEx log and saves a picture of the VR UI texture. Include both when reporting a problem.");
            KeyDesktopView = config.Bind("Hotkeys", "Desktop View On/Off", Key.F11,
                "Turns the game picture on your monitor on or off while in VR (Fixes > Desktop View On). Can also be changed in the VR tab.");

            ControlFromVrView = config.Bind("Testing", "Control From VR View", false,
                "Testing only, for SteamVR's null driver (no headset). While SteamVR's VR View window is the focused window, pass your keyboard, " +
                "mouse and mouse-look to the game. Leave this off when you play with a real headset.");

            SubmitOverlayOnRenderThread = config.Bind("Debug", "Submit Overlay On Render Thread", true,
                "Advanced. Sends the UI image to SteamVR from Unity's render thread (correct ordering). Turn off only if the game crashes when the SteamVR overlay is used.");
            VerboseLogging = config.Bind("Debug", "Verbose Logging", false,
                "Logs extra detail about every change the plugin makes, and what is under the mouse on every click. Leave off unless you are diagnosing a problem.");

            _settingsVersion = config.Bind("Debug", "Settings Version", 0,
                "Used by the plugin to update settings saved by older versions. Don't change it.");
            Migrate(config);
        }

        /// <summary>One line with the settings players change most, for the start-up log.</summary>
        public static string Describe()
        {
            return string.Format(
                "UI {0:0}° wide at {1:0.0} m (anchor {2}, height {3:0.00} m), display {4}, darken behind menus {5:0%}, cursor x{6:0.0}, " +
                "game window {7} {8} {9:0}° wide aimed {10:0}°, monitor V-Sync off in VR {11}, DLSS {12} (jitter {13}), control from VR View {14}",
                PanelWidthDegrees.Value, PanelDistance.Value, Anchor.Value, PanelHeightOffset.Value, DisplayMode.Value,
                MenuBackdropOpacity.Value, CursorScale.Value, DesktopViewOn.Value ? "on" : "off", DesktopView.Value, DesktopViewWidth.Value,
                DesktopViewHeight.Value, OnOff(MonitorVSyncOffInVr.Value), DisableDlss.Value ? "off" : "allowed", OnOff(DlssCameraJitter.Value),
                OnOff(ControlFromVrView.Value));
        }

        private static string OnOff(bool on)
        {
            return on ? "on" : "off";
        }

        /// <summary>Updates values saved by older plugin versions whose defaults turned out to be wrong.</summary>
        private static void Migrate(ConfigFile config)
        {
            int version = _settingsVersion.Value;
            if (version >= CurrentSettingsVersion) return;

            if (version < 2 && MenuBackdropOpacity.Value == 0.5f)
            {
                // 3.0.0 darkened the panel behind menus by default, which showed as a grey box.
                MenuBackdropOpacity.Value = 0f;
                Log.Info("Settings updated from an older version: Menu Backdrop Opacity 0.5 -> 0 (new default).");
            }
            if (version < 3 && MenuBackdropOpacity.Value >= 0.95f)
            {
                // The old VR tab slider was named "Menu Background Dimming" and went to 100%, which turns everything
                // behind menus black (the main menu looked broken). The VR tab now stops at 80%.
                MenuBackdropOpacity.Value = 0f;
                Log.Info("Settings updated from an older version: Menu Backdrop Opacity 1 -> 0. At 100% the world behind menus was completely black.");
            }
            if (version < 4)
            {
                // Single-pass stereo was tried in 3.8.0 and removed: the two eyes' views didn't turn together.
                // The old desktop switch is from 3.0. BepInEx keeps unknown lines in the file, so they are tidied away here.
                RemoveObsolete(config, new ConfigDefinition("Performance", "Single-Pass Stereo (Experimental)"),
                    new ConfigDefinition("Fixes", "Show VR View On Desktop"));
            }
            _settingsVersion.Value = CurrentSettingsVersion;
        }

        /// <summary>Removes lines for settings that no longer exist from the config file.</summary>
        private static void RemoveObsolete(ConfigFile config, params ConfigDefinition[] keys)
        {
            try
            {
                var orphans = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config, null) as IDictionary<ConfigDefinition, string>;
                if (orphans == null) return;
                var removed = new List<string>();
                foreach (ConfigDefinition key in keys)
                {
                    if (orphans.Remove(key)) removed.Add(key.Key);
                }
                if (removed.Count == 0) return;
                config.Save();
                Log.Info("Removed settings that no longer exist from the config file: " + string.Join(", ", removed.ToArray()) + ".");
            }
            catch (Exception e)
            {
                Log.Verbose("Could not remove old settings from the config file: " + e.Message);
            }
        }
    }
}
