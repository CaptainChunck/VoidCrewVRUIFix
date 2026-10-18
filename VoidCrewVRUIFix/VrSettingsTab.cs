using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using CG.Client;
using HarmonyLib;
using UI;
using UI.Core;
using UI.Core.Audio;
using UI.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Adds a "VR" tab to the game's own Settings menu, built from the game's own setting rows (toggles, sliders and
    /// dropdowns), so it looks and sounds exactly like the other tabs. Every row saves straight into this plugin's
    /// config (or UUVR's) and takes effect immediately, so changes can be judged live in the headset.
    /// At the top, a MODIFY UI button opens the Modify UI window (UiEditorWindow), where every movable piece of the VR UI
    /// is placed. The Settings menu's RESTORE button, pressed on this tab, puts all of it back to the defaults.
    /// This and that window are the only parts of the plugin that use the game's UI classes directly; if a game update
    /// changes them, installing the tab fails with one warning and everything else keeps working.
    /// </summary>
    internal static class VrSettingsTab
    {
        private const string PanelName = "VoidCrewVRUIFix_VRSettingsPanel";

        /// <summary>The VR tab's limit for menu darkening; at 100% the world behind menus is simply black.</summary>
        private const float MaxBackdropOpacity = 0.8f;

        /// <summary>Slider number format with a degree sign.</summary>
        internal const string Degrees = "0\u00b0";

        // One list of row refreshers per Settings menu: the game can create more than one (main menu and in game).
        private static readonly Dictionary<VisualElement, List<Action>> Refreshers = new Dictionary<VisualElement, List<Action>>();

        public static void Install(MonoBehaviour settingsScreen)
        {
            var document = settingsScreen.GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null || root.Q(PanelName) != null) return;

            // The VR tab goes straight after GRAPHICS.
            var tabs = root.Q<TabsRibbon>();
            VisualElement graphicsPanel = root.Q("GraphicsSettingsPanel");
            VisualElement generalPanel = root.Q("GeneralSettingsPanel");
            if (tabs == null || graphicsPanel == null || generalPanel == null || graphicsPanel.parent == null)
            {
                Log.Warn("The game's Settings menu layout has changed; the VR settings tab was not added. Use the config file or Config Manager (F5) instead.");
                return;
            }

            // The ribbon shows the tab container's Nth child for the Nth header, so work with those top-level children.
            VisualElement tabContainer = tabs.TabContainer ?? graphicsPanel.parent;
            VisualElement graphicsTab = TopLevelTab(tabContainer, graphicsPanel);
            VisualElement generalTab = TopLevelTab(tabContainer, generalPanel);
            if (graphicsTab == null || generalTab == null)
            {
                Log.Warn("The game's Settings tabs are laid out differently than expected; the VR settings tab was not added.");
                return;
            }
            int index = tabContainer.hierarchy.IndexOf(graphicsTab) + 1;

            VisualElement rows;
            VisualElement panel = CreatePanel(document, generalPanel, Depth(generalPanel, generalTab), out rows);
            panel.name = PanelName;
            panel.style.display = DisplayStyle.None;
            var refreshers = new List<Action>();
            // The Modify UI window covers the Settings menu's frame (the tabs and their contents) while it is open.
            var editor = new UiEditorWindow(tabs.parent ?? root);
            AddModifyUiButton(rows, editor);
            AddRows(rows, refreshers);
            ForgetClosedMenus();
            Refreshers[root] = refreshers;
            tabContainer.hierarchy.Insert(index, panel);

            AddHeader(tabs, index);
            AnnounceForLocalisation(panel, root.panel);
            Log.Info("Added the VR tab to the game's Settings menu.");
        }

        /// <summary>
        /// The Settings menu's RESTORE button. On the VR tab it asks first, then puts the VR tab and the Modify UI window
        /// back to their defaults. The game's own restore resets every game setting and key binding but none of the VR
        /// ones, so it only runs on the game's tabs. Returns true if this handled the click.
        /// </summary>
        public static bool TryRestore(MonoBehaviour settingsScreen)
        {
            var document = settingsScreen.GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            VisualElement panel = root != null ? root.Q(PanelName) : null;
            if (panel == null || !IsShowing(panel)) return false;

            MenuScreenController.Instance.ShowConfirmationPopup("RESTORE VR SETTINGS?",
                "Puts every setting on the VR tab, the UI screen and every piece in Modify UI back to the defaults. " +
                "Your other settings and key bindings stay as they are.",
                () =>
                {
                    Settings.RestoreVrDefaults();
                    UuvrCompat.ApplyDefaults(true);
                    List<Action> refreshers;
                    if (Refreshers.TryGetValue(root, out refreshers))
                    {
                        foreach (Action refresh in refreshers) refresh();
                    }
                    UiEditorWindow.RefreshAll();
                    Log.Info("VR tab: RESTORE put the VR settings, the UI screen and the Modify UI pieces back to their defaults.");
                });
            return true;
        }

        /// <summary>Called when a Settings menu opens, so its VR tab shows the current values (changed elsewhere, or by F3).</summary>
        public static void Refresh(MonoBehaviour settingsScreen)
        {
            UiEditorWindow.CloseAll();
            var document = settingsScreen.GetComponent<UIDocument>();
            List<Action> refreshers;
            if (document == null || document.rootVisualElement == null || !Refreshers.TryGetValue(document.rootVisualElement, out refreshers)) return;
            foreach (Action refresh in refreshers) refresh();
        }

        private static void ForgetClosedMenus()
        {
            var closed = new List<VisualElement>();
            foreach (VisualElement root in Refreshers.Keys)
            {
                if (root.panel == null) closed.Add(root);
            }
            foreach (VisualElement root in closed) Refreshers.Remove(root);
        }

        /// <summary>The tab container's direct child that holds this element, or null if it isn't inside the container.</summary>
        private static VisualElement TopLevelTab(VisualElement tabContainer, VisualElement element)
        {
            while (element != null && element.hierarchy.parent != tabContainer) element = element.hierarchy.parent;
            return element;
        }

        private static int Depth(VisualElement element, VisualElement ancestor)
        {
            int depth = 0;
            for (; element != null && element != ancestor; element = element.hierarchy.parent) depth++;
            return depth;
        }

        /// <summary>
        /// A copy of the General tab (same styles, scrolling and layout), emptied of its rows.
        /// Made from the menu's own layout file, so it matches exactly.
        /// </summary>
        private static VisualElement CreatePanel(UIDocument document, VisualElement generalPanel, int panelDepth, out VisualElement rows)
        {
            VisualTreeAsset layout = document.visualTreeAsset;
            VisualElement copy = layout != null ? layout.Instantiate().Q("GeneralSettingsPanel") : null;
            for (int i = 0; copy != null && i < panelDepth; i++) copy = copy.hierarchy.parent;
            if (copy != null)
            {
                copy.RemoveFromHierarchy();
                SettingEntryVE firstRow = copy.Q<SettingEntryVE>();
                rows = firstRow != null ? firstRow.parent : copy.contentContainer;
                rows.Clear();

                // Anything else of the General tab (rows outside the row list, the player ID field) goes too.
                foreach (VisualElement leftover in copy.Query<VisualElement>(null, "settings-entry").ToList()) leftover.RemoveFromHierarchy();
                foreach (TextField leftover in copy.Query<TextField>().ToList()) leftover.RemoveFromHierarchy();

                foreach (VisualElement element in copy.Query<VisualElement>().ToList())
                {
                    // The layout file names its elements; give the copies their own names so the game never finds them.
                    if (!string.IsNullOrEmpty(element.name) && !element.name.StartsWith("unity-")) element.name = "VR_" + element.name;
                }
                return copy;
            }

            // No layout file available: rebuild the General tab's container from its style classes.
            var panel = new ScrollView(ScrollViewMode.Vertical);
            foreach (string styleClass in generalPanel.GetClasses()) panel.AddToClassList(styleClass);
            rows = panel.contentContainer;
            return panel;
        }

        private static void AddHeader(TabsRibbon tabs, int index)
        {
            VisualElement template = tabs.Headers != null && tabs.Headers.Count > 0 ? tabs.Headers[0] : null;
            var header = new Label("VR") { name = "VoidCrewVRUIFix_VRTab", focusable = true };
            if (template != null)
            {
                foreach (string styleClass in template.GetClasses())
                {
                    if (styleClass != "tabs-header__selected") header.AddToClassList(styleClass);
                }
            }
            header.AddToClassList("tabs-header");

            // Put the header after GRAPHICS, keeping the gap elements the ribbon uses between headers.
            VisualElement previous = tabs.Headers != null && tabs.Headers.Count >= index ? tabs.Headers[index - 1] : null;
            VisualElement headerRow = tabs.HeaderContainer ?? tabs.contentContainer;
            int position = previous != null ? headerRow.IndexOf(previous) + 1 : headerRow.childCount;
            if (tabs.SpacingObjects)
            {
                var spacing = new VisualElement { name = "SpacingObject" };
                spacing.AddToClassList("tabs-spacing-object");
                headerRow.Insert(position++, spacing);
            }
            headerRow.Insert(position, header);

            if (tabs.Headers != null)
            {
                tabs.Headers.Insert(index, header);
                header.AddUnfilteredClickable(new Clickable(() => tabs.SelectHeader(tabs.Headers.IndexOf(header))));
                UIAudioProvider.AddSounds(header, "tabs");
            }
        }

        private static void AddRows(VisualElement rows, List<Action> refreshers)
        {
            AddToggle(rows, refreshers, "VR ON / OFF (THIS SESSION)",
                "Turns VR off or back on without restarting, like UUVR's F3 key. The game always starts in VR, so this isn't remembered after a restart.",
                () => XrState.IsActive, on =>
                {
                    if (on != XrState.IsActive && !UuvrCompat.ToggleVr())
                    {
                        Log.Warn("Could not switch VR " + (on ? "on" : "off") + " (UUVR not found). Use UUVR's F3 key instead.");
                    }
                });
            AddDropdown(rows, refreshers, "VR SYSTEM (NEEDS RESTART)",
                "Which VR system UUVR uses. OpenVR (SteamVR, the default) is recommended: it lets the UI be shown as a sharp SteamVR overlay. Restart the game after changing this.",
                new[] { "OpenXR", "OpenVR (SteamVR)" },
                () => UuvrCompat.VrApi == "OpenVr" ? 1 : 0,
                choice => UuvrCompat.SetSetting("PreferredVrApi", choice == 1 ? "OpenVr" : "OpenXr"));
            AddDropdown(rows, refreshers, "UI SHOWN AS",
                "How menus and the HUD reach your headset. SteamVR overlay (default): SteamVR draws the UI on its own floating screen, the sharpest option " +
                "(with OpenXR the UI is drawn into the game image instead). Automatic uses the SteamVR overlay when it can. " +
                "Drawn into the game image: also works with OpenXR, but is a little softer. F7 switches this too.",
                new[] { "Automatic (best)", "SteamVR overlay (sharpest)", "Drawn into the game image" },
                () => (int)Settings.DisplayMode.Value, choice => Settings.DisplayMode.Value = (UiDisplayMode)choice);
            AddToggle(rows, refreshers, "AIM WITH YOUR HEAD",
                "On (default): while you play, the crosshair stays in the middle of your view, and your weapons, tools and 'press E' interactions aim where you look. " +
                "Your body still turns with the mouse. In a turret, the turret turns to wherever you look and the mouse turns your view. " +
                "Menus and terminals stay put in front of you. Off: you aim with the mouse only.",
                () => Settings.AimWithHead.Value, on => Settings.AimWithHead.Value = on);
            AddSlider(rows, refreshers, "DARKEN WORLD BEHIND MENUS",
                "While a menu or terminal is open, puts a dark layer over the game world behind it, which can make menu text easier to read (default 0%, off). " +
                "Menus that already darken the screen, like Settings, only get slightly darker.",
                0f, MaxBackdropOpacity, SliderFloatSettingEntryVE.NumberDisplayMode.Percentage, "F0",
                () => Settings.MenuBackdropOpacity.Value, v => Settings.MenuBackdropOpacity.Value = Mathf.Round(v * 20f) / 20f);
            AddSlider(rows, refreshers, "VR MOUSE POINTER SIZE",
                "Size of the mouse pointer drawn on the UI screen in VR (default " + ((float)Settings.CursorScale.DefaultValue).ToString("0.0") + "X).",
                0.5f, 4f, SliderFloatSettingEntryVE.NumberDisplayMode.Multiplier, "0.0",
                () => Settings.CursorScale.Value, v => Settings.CursorScale.Value = Mathf.Round(v * 10f) / 10f);
            AddToggle(rows, refreshers, "KEEP HORIZON LEVEL (UUVR)",
                "A UUVR setting. On: the game only turns you left and right, and you look up and down with your head, which some people find more comfortable. " +
                "Off (default): the mouse tilts the view up and down as usual.",
                () => UuvrCompat.GetSetting("AlignCameraToHorizon") == "True", on => UuvrCompat.SetSetting("AlignCameraToHorizon", on ? "True" : "False"));
            AddToggle(rows, refreshers, "SHOW GAME ON MONITOR",
                "Shows the game in the game window on your monitor while you play in VR (default on). Off leaves the window black and skips drawing it, " +
                "which saves a little graphics work: a small frame-rate gain, not a big one. The key below switches it too.",
                () => Settings.DesktopViewOn.Value, on => Settings.DesktopViewOn.Value = on);
            AddKey(rows, refreshers, "SHOW GAME ON MONITOR KEY",
                "The key that turns the game picture on your monitor on and off while you play in VR (default F11). Click, then press the key you want. " +
                "Backspace or Delete removes the key, Escape cancels.",
                Settings.KeyDesktopView);
            AddToggle(rows, refreshers, "SHOW GAME ON MONITOR WHEN HEADSET IS OFF",
                "When the monitor picture is off, shows it again while the headset is taken off (using the headset's proximity sensor), " +
                "and turns it off again when you put the headset back on (default on).",
                () => Settings.DesktopViewWhenHeadsetOff.Value, on => Settings.DesktopViewWhenHeadsetOff.Value = on);
            AddDropdown(rows, refreshers, "MONITOR WINDOW SHOWS",
                "What the game window on your monitor shows while you play in VR. Headset view with UI: what you see in the headset, menus included (default). " +
                "Headset view, no UI: Unity's plain mirror of one eye. UI only: just the menus and HUD on black.",
                new[] { "Headset view with UI", "Headset view, no UI", "UI only" },
                () => (int)Settings.DesktopView.Value, choice => Settings.DesktopView.Value = (DesktopViewMode)choice);
            AddSlider(rows, refreshers, "MONITOR WINDOW VIEW WIDTH",
                "How much of the headset view the monitor window shows when it shows the headset view with UI (default " + ((float)Settings.DesktopViewWidth.DefaultValue).ToString("0") +
                "). Lower zooms in; higher shows more around the edges. " +
                "Set it to the UI screen size to make the UI fill the window.",
                30f, 170f, SliderFloatSettingEntryVE.NumberDisplayMode.Value, Degrees,
                () => Settings.DesktopViewWidth.Value, v => Settings.DesktopViewWidth.Value = Mathf.Round(v));
            AddSlider(rows, refreshers, "MONITOR WINDOW VIEW HEIGHT",
                "Aims the monitor window's view up or down (default 0, centred on the UI screen). Higher shows more of what's above the UI and moves the UI down the window; " +
                "lower does the opposite.",
                -30f, 30f, SliderFloatSettingEntryVE.NumberDisplayMode.Value, Degrees,
                () => Settings.DesktopViewHeight.Value, v => Settings.DesktopViewHeight.Value = Mathf.Round(v));
            AddToggle(rows, refreshers, "FPS COUNTER IN HEADSET",
                "Shows the game's frame rate in your view in the headset (default off; Modify UI places it). Green: the headset's full refresh rate. " +
                "Yellow: half of it or more, so SteamVR is showing each frame twice. Red: lower than that.",
                () => Settings.ShowFpsCounter.Value, on => Settings.ShowFpsCounter.Value = on);
            AddToggle(rows, refreshers, "MONITOR V-SYNC OFF IN VR",
                "Turns off the monitor's V-Sync and the game's frame limit while you play in VR, so your monitor's refresh rate can never hold back the headset. " +
                "SteamVR paces the frames instead. Your own settings come back when VR is turned off (default on).",
                () => Settings.MonitorVSyncOffInVr.Value, on => Settings.MonitorVSyncOffInVr.Value = on);
            AddToggle(rows, refreshers, "ALLOW DLSS (EXPERIMENTAL, NEEDS RESTART)",
                "Keeps DLSS in Graphics > Upscaling (default on). DLSS renders the game at a lower resolution and upscales it, which can raise the frame rate on NVIDIA RTX cards. " +
                "It's experimental in VR: choose DLSS under Graphics > Upscaling to use it. Turn this off and restart the game if DLSS causes problems in VR.",
                () => !Settings.DisableDlss.Value, on => Settings.DisableDlss.Value = !on);
            AddToggle(rows, refreshers, "SHARPER DLSS (CAMERA JITTER)",
                "Gives DLSS the tiny per-frame camera jitter it needs to rebuild fine detail. HDRP leaves jitter out in VR, which makes DLSS blurry. " +
                "Only used while DLSS is running (default on). Turn it off if edges shimmer.",
                () => Settings.DlssCameraJitter.Value, on => Settings.DlssCameraJitter.Value = on);
            AddToggle(rows, refreshers, "TESTING: CONTROL FROM STEAMVR VR VIEW",
                "Only for testing without a headset, using SteamVR's null driver. While SteamVR's VR View window is the focused window, your keyboard and mouse are passed to the game. " +
                "Leave this off when you play with a real headset.",
                () => Settings.ControlFromVrView.Value, on => Settings.ControlFromVrView.Value = on);
        }

        /// <summary>The prominent button at the top of the VR tab that opens the Modify UI window.</summary>
        private static void AddModifyUiButton(VisualElement rows, UiEditorWindow editor)
        {
            var block = new VisualElement { name = "VoidCrewVRUIFix_ModifyUi" };
            block.style.marginTop = 8f;
            block.style.marginBottom = 18f;

            var button = new Button(editor.Open) { text = "MODIFY UI", name = "VoidCrewVRUIFix_ModifyUiButton" };
            button.style.height = 72f;
            button.style.fontSize = 30f;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.color = Color.white;
            button.style.backgroundColor = new Color(0.12f, 0.42f, 0.6f, 1f);
            var edge = new Color(0.39f, 0.75f, 0.93f, 1f);
            button.style.borderLeftWidth = 3f;
            button.style.borderRightWidth = 3f;
            button.style.borderTopWidth = 3f;
            button.style.borderBottomWidth = 3f;
            button.style.borderLeftColor = edge;
            button.style.borderRightColor = edge;
            button.style.borderTopColor = edge;
            button.style.borderBottomColor = edge;
            UIAudioProvider.AddSound(button, UIAudioEventRef.ESoundType.Click, "list");
            UIAudioProvider.AddSound(button, UIAudioEventRef.ESoundType.Hover, "list");
            block.Add(button);

            var note = new Label("Move, resize and lock to your head every piece of the HUD and the UI screen, with a picture of your view.");
            note.style.fontSize = 17f;
            note.style.color = new Color(0.75f, 0.8f, 0.85f, 1f);
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.unityTextAlign = TextAnchor.MiddleCenter;
            note.style.marginTop = 6f;
            block.Add(note);
            rows.Add(block);
        }

        /// <param name="logName">The name the log uses for the setting (the label if not given).</param>
        internal static void AddToggle(VisualElement rows, List<Action> refreshers, string label, string help, Func<bool> read, Action<bool> write, string logName = null)
        {
            var row = new BooleanSettingsEntryVE();
            var value = new IntSetting(read() ? 1 : 0, 0, 1, false);
            Prepare(row, label, help);
            row.Init(value);
            ShowToggleState(row, value.Value == 1);
            value.OnValueChanged += v => Apply(logName ?? label, v == 1 ? "on" : "off", () => write(v == 1));
            refreshers.Add(() =>
            {
                value.SetValue(read() ? 1 : 0, true);
                row.Reload();
                ShowToggleState(row, value.Value == 1);
            });
            rows.Add(row);
        }

        /// <summary>
        /// The game's toggle row only restyles itself (ENABLED/DISABLED) from a change event, and a row that isn't on
        /// screen yet gets none, so a saved "on" used to show as off, and clicking it turned the setting off.
        /// </summary>
        private static void ShowToggleState(VisualElement row, bool on)
        {
            row.EnableInClassList("settings-toggle__true", on);
            row.EnableInClassList("settings-toggle__false", !on);
        }

        /// <param name="logName">The name the log uses for the setting (the label if not given).</param>
        internal static void AddSlider(VisualElement rows, List<Action> refreshers, string label, string help, float min, float max,
            SliderFloatSettingEntryVE.NumberDisplayMode mode, string format, Func<float> read, Action<float> write, string logName = null)
        {
            var row = new SliderFloatSettingEntryVE();
            AccessTools.Property(typeof(SliderFloatSettingEntryVE), "DisplayMode")?.SetValue(row, mode, null);
            AccessTools.Property(typeof(SliderFloatSettingEntryVE), "ValueCulture")?.SetValue(row, format, null);
            AccessTools.Property(typeof(SliderFloatSettingEntryVE), "LowValue")?.SetValue(row, min, null);
            AccessTools.Property(typeof(SliderFloatSettingEntryVE), "HighValue")?.SetValue(row, max, null);
            row.slider.lowValue = min;
            row.slider.highValue = max;

            var value = new FloatSetting(Mathf.Clamp(read(), min, max), min, max, false);
            Prepare(row, label, help);
            row.Init(value);
            string logFormat = mode == SliderFloatSettingEntryVE.NumberDisplayMode.Percentage ? "0%" : format;
            value.OnValueChanged += v => Apply(logName ?? label, v.ToString(logFormat), () => write(v));
            refreshers.Add(() =>
            {
                value.SetValue(Mathf.Clamp(read(), min, max), true);
                row.Reload();
            });
            rows.Add(row);
        }

        private static void AddDropdown(VisualElement rows, List<Action> refreshers, string label, string help, string[] choices, Func<int> read, Action<int> write)
        {
            var row = new DropdownSettingEntryVE();
            var list = new List<string>(choices);
            var value = new StringListSetting(list, Mathf.Clamp(read(), 0, list.Count - 1), false);
            Prepare(row, label, help);
            row.Init(value);
            value.OnValueChanged += chosen => Apply(label, chosen, () => write(list.IndexOf(chosen)));
            refreshers.Add(() =>
            {
                value.SetValue(Mathf.Clamp(read(), 0, list.Count - 1), true);
                row.Reload();
            });
            rows.Add(row);
        }

        private static void AddKey(VisualElement rows, List<Action> refreshers, string label, string help, ConfigEntry<UnityEngine.InputSystem.Key> entry)
        {
            var row = new KeyRow();
            Prepare(row, label, help);
            row.KeyText = HotkeyCapture.KeyName(entry.Value);

            int lastClick = -1;
            row.Clicked += () =>
            {
                // One click can reach the button twice (the row passes its clicks on to the button).
                if (Time.frameCount == lastClick) return;
                lastClick = Time.frameCount;
                if (HotkeyCapture.IsWaitingFor(entry))
                {
                    HotkeyCapture.Cancel();
                    return;
                }
                row.KeyText = "PRESS A KEY";
                HotkeyCapture.Begin(entry, () => row.KeyText = HotkeyCapture.KeyName(entry.Value), () => IsShowing(row));
            };
            refreshers.Add(() =>
            {
                if (!HotkeyCapture.IsWaitingFor(entry)) row.KeyText = HotkeyCapture.KeyName(entry.Value);
            });
            rows.Add(row);
        }

        /// <summary>False once the element's menu is closed or another tab is showing.</summary>
        internal static bool IsShowing(VisualElement element)
        {
            if (element.panel == null) return false;
            for (VisualElement e = element; e != null; e = e.hierarchy.parent)
            {
                IResolvedStyle style = e.resolvedStyle;
                if (style.display == DisplayStyle.None || style.visibility == Visibility.Hidden || style.opacity < 0.01f) return false;
            }
            return true;
        }

        /// <summary>
        /// A setting row with a key button, built the same way as the game's own key-binding rows. Clicking it waits for
        /// the next key press (see HotkeyCapture) instead of opening the game's rebinding popup.
        /// </summary>
        private sealed class KeyRow : SettingEntryVE
        {
            private Button _keyButton;

            public event Action Clicked;

            public string KeyText
            {
                set { if (_keyButton != null) _keyButton.text = value; }
            }

            // Called by the base constructor.
            protected override void SetupContainers()
            {
                base.SetupContainers();
                AddToClassList("settings-entry-type__key-bind");
                _keyButton = VEFactoryHelper.SetupVE<Button>(inputContainer, "KeyBindButton", "settings-key-bind");
                UIAudioProvider.AddSound(_keyButton, UIAudioEventRef.ESoundType.Click, "list");
                UIAudioProvider.AddSound(_keyButton, UIAudioEventRef.ESoundType.Hover, "list");
                inputContainer.Insert(1, _keyButton);
                _keyButton.clicked += () =>
                {
                    Action clicked = Clicked;
                    if (clicked != null) clicked();
                };
                activeElement = _keyButton;
            }

            public override void Init(ISetting setting)
            {
            }

            public override void Reload()
            {
            }
        }

        private static void Prepare(SettingEntryVE row, string label, string help)
        {
            row.SettingName = "VoidCrewVRUIFix " + label;
            Label text = row.Q<Label>("SettingsLabel");
            if (text != null) text.text = label;

            // The game's own "?" tooltip next to the name, which it looks up by the row's setting name.
            SettingsTooltipsList.Tooltips[row.SettingName] = new DefaultableLocalizedString { FallBackString = help };
            row.AddOrUpdateTooltip(row.SettingName);
        }

        private static void Apply(string label, string shown, Action change)
        {
            try
            {
                change();
                Log.InfoDebounced("vr-tab:" + label, "VR tab: " + label + " set to " + shown + ".");
            }
            catch (Exception e)
            {
                Log.Failure("Changing the VR setting '" + label + "'", e);
            }
        }

        /// <summary>The game translates "#key" texts when told an element was added; do that for the new tab.</summary>
        private static void AnnounceForLocalisation(VisualElement panel, IPanel targetPanel)
        {
            ViewEventBus bus = ViewEventBus.Instance;
            if (bus == null || targetPanel == null) return;
            foreach (VisualElement element in panel.Query<VisualElement>().ToList())
            {
                var text = element as TextElement;
                if ((text != null && text.text != null && text.text.StartsWith("#")) || element is DropdownField)
                {
                    bus.VEUpdatedInPanel.Publish(element, targetPanel);
                }
            }
        }
    }
}
