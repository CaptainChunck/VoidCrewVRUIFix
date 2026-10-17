using BepInEx.Configuration;
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

        public static ConfigEntry<bool> ShowVrCursor;
        public static ConfigEntry<float> CursorScale;
        public static ConfigEntry<bool> HideHardwareCursor;

        public static ConfigEntry<bool> AlignMarkersToPanel;
        public static ConfigEntry<bool> MoveCameraCanvasesToPanel;
        public static ConfigEntry<bool> FixIntroVideo;
        public static ConfigEntry<bool> DisableDlss;
        public static ConfigEntry<bool> DisableFsr3;
        public static ConfigEntry<bool> DisableUuvrUi;
        public static ConfigEntry<bool> DisableUuvrHelperCameras;
        public static ConfigEntry<DesktopViewMode> DesktopView;
        public static ConfigEntry<float> DesktopViewWidth;
        public static ConfigEntry<bool> ClearDesktopEachFrame;

        public static ConfigEntry<Key> KeyFlip;
        public static ConfigEntry<Key> KeyDisplayMode;
        public static ConfigEntry<Key> KeyRecenter;
        public static ConfigEntry<Key> KeyDiagnostics;

        public static ConfigEntry<bool> ControlFromVrView;

        public static ConfigEntry<bool> SubmitOverlayOnRenderThread;
        public static ConfigEntry<bool> VerboseLogging;
        private static ConfigEntry<int> _settingsVersion;

        private const int CurrentSettingsVersion = 2;

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
                new ConfigDescription("Darkens the whole panel behind menus (only while the mouse cursor is shown). 0 = off. Try 0.3 if menu text is hard to read against bright scenes.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FlipVertically = config.Bind("UI Panel", "Flip Vertically", false,
                "Turn on if the UI appears upside down in the headset. Can also be toggled in game with the Flip UI hotkey.");

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
            ClearDesktopEachFrame = config.Bind("Fixes", "Clear Desktop Each Frame", true,
                "Clears the game window every frame. Stops old UI images being left behind as 'ghosts' or trails on the desktop.");

            KeyFlip = config.Bind("Hotkeys", "Flip UI", Key.F6,
                "Flips the VR UI panel upside down (use if it appears inverted). Set to None to disable.");
            KeyDisplayMode = config.Bind("Hotkeys", "Switch Display Mode", Key.F7,
                "Switches between the SteamVR overlay and the in-game panel.");
            KeyRecenter = config.Bind("Hotkeys", "Recenter UI", Key.F8,
                "Moves the UI panel back in front of you.");
            KeyDiagnostics = config.Bind("Hotkeys", "Write Diagnostics", Key.F9,
                "Writes a detailed diagnostics report to the BepInEx log and saves a picture of the VR UI texture. Include both when reporting a problem.");

            ControlFromVrView = config.Bind("Testing", "Control From VR View", true,
                "While SteamVR's VR View window is the focused window, pass your keyboard, mouse buttons and mouse-look to the game. " +
                "Useful for testing without a headset. Has no effect while the game window itself is focused.");

            SubmitOverlayOnRenderThread = config.Bind("Debug", "Submit Overlay On Render Thread", true,
                "Advanced. Sends the UI image to SteamVR from Unity's render thread (correct ordering). Turn off only if the game crashes when the SteamVR overlay is used.");
            VerboseLogging = config.Bind("Debug", "Verbose Logging", false,
                "Logs extra detail about every change the plugin makes, and what is under the mouse on every click. Leave off unless you are diagnosing a problem.");

            _settingsVersion = config.Bind("Debug", "Settings Version", 0,
                "Used by the plugin to update settings saved by older versions. Don't change it.");
            Migrate();
        }

        /// <summary>Updates values saved by older plugin versions whose defaults turned out to be wrong.</summary>
        private static void Migrate()
        {
            int version = _settingsVersion.Value;
            if (version >= CurrentSettingsVersion) return;

            if (version < 2 && MenuBackdropOpacity.Value == 0.5f)
            {
                // 3.0.0 darkened the panel behind menus by default, which showed as a grey box.
                MenuBackdropOpacity.Value = 0f;
                Log.Info("Settings updated from an older version: Menu Backdrop Opacity 0.5 -> 0 (new default).");
            }
            _settingsVersion.Value = CurrentSettingsVersion;
        }
    }
}
