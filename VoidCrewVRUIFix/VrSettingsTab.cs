using System;
using System.Collections.Generic;
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
    /// This is the only part of the plugin that uses the game's UI classes directly; if a game update changes them,
    /// installing the tab fails with one warning and everything else keeps working.
    /// </summary>
    internal static class VrSettingsTab
    {
        private const string PanelName = "VoidCrewVRUIFix_VRSettingsPanel";

        /// <summary>The VR tab's limit for menu darkening; at 100% the world behind menus is simply black.</summary>
        private const float MaxBackdropOpacity = 0.8f;

        /// <summary>Slider number format with a degree sign.</summary>
        private const string Degrees = "0\u00b0";

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
            AddRows(rows, refreshers);
            ForgetClosedMenus();
            Refreshers[root] = refreshers;
            tabContainer.hierarchy.Insert(index, panel);

            AddHeader(tabs, index);
            AnnounceForLocalisation(panel, root.panel);
            Log.Info("Added the VR tab to the game's Settings menu.");
        }

        /// <summary>Called when a Settings menu opens, so its VR tab shows the current values (changed elsewhere, or by F3).</summary>
        public static void Refresh(MonoBehaviour settingsScreen)
        {
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
                "Which VR system UUVR uses. OpenVR (SteamVR) is recommended: it lets the UI be shown as a sharp SteamVR overlay. Restart the game after changing this.",
                new[] { "OpenXR", "OpenVR (SteamVR)" },
                () => UuvrCompat.VrApi == "OpenVr" ? 1 : 0,
                choice => UuvrCompat.SetSetting("PreferredVrApi", choice == 1 ? "OpenVr" : "OpenXr"));
            AddDropdown(rows, refreshers, "UI SHOWN AS",
                "How menus and the HUD reach your headset. Automatic uses the SteamVR overlay when it can. SteamVR overlay: SteamVR draws the UI on its own floating screen, the sharpest option. " +
                "Drawn into the game image: also works with OpenXR, but is a little softer. F7 switches this too.",
                new[] { "Automatic (best)", "SteamVR overlay (sharpest)", "Drawn into the game image" },
                () => (int)Settings.DisplayMode.Value, choice => Settings.DisplayMode.Value = (UiDisplayMode)choice);
            AddSlider(rows, refreshers, "UI SCREEN SIZE",
                "How wide the floating UI screen looks, as an angle of your view (default 70). Bigger is easier to read; smaller keeps the edges of menus in sight.",
                30f, 120f, SliderFloatSettingEntryVE.NumberDisplayMode.Value, Degrees,
                () => Settings.PanelWidthDegrees.Value, v => Settings.PanelWidthDegrees.Value = Mathf.Round(v));
            AddSlider(rows, refreshers, "UI SCREEN DISTANCE",
                "How far away the UI screen floats (default 1.5 m). It looks the same size at any distance, so this only changes how deep it sits compared with the game world.",
                0.5f, 5f, SliderFloatSettingEntryVE.NumberDisplayMode.Value, "0.0 m",
                () => Settings.PanelDistance.Value, v => Settings.PanelDistance.Value = Mathf.Round(v * 10f) / 10f);
            AddSlider(rows, refreshers, "UI SCREEN HEIGHT",
                "Moves the UI screen up or down, in metres. 0 keeps it centred on your view.",
                -1f, 1f, SliderFloatSettingEntryVE.NumberDisplayMode.Value, "0.00 m",
                () => Settings.PanelHeightOffset.Value, v => Settings.PanelHeightOffset.Value = Mathf.Round(v * 20f) / 20f);
            AddToggle(rows, refreshers, "UI SCREEN FOLLOWS YOUR HEAD",
                "On: the UI screen stays in front of your eyes wherever you look. Off (default): it stays in front of the game camera, so you can look around it, and markers and pings line up with the world.",
                () => Settings.Anchor.Value == PanelAnchor.Head, on => Settings.Anchor.Value = on ? PanelAnchor.Head : PanelAnchor.GameCamera);
            AddSlider(rows, refreshers, "DARKEN WORLD BEHIND MENUS",
                "While a menu or terminal is open, puts a dark layer over the game world behind it, which can make menu text easier to read (default 0%, off). " +
                "Menus that already darken the screen, like Settings, only get slightly darker.",
                0f, MaxBackdropOpacity, SliderFloatSettingEntryVE.NumberDisplayMode.Percentage, "F0",
                () => Settings.MenuBackdropOpacity.Value, v => Settings.MenuBackdropOpacity.Value = Mathf.Round(v * 20f) / 20f);
            AddSlider(rows, refreshers, "VR MOUSE POINTER SIZE",
                "Size of the mouse pointer drawn on the UI screen in VR (default 1.0X).",
                0.5f, 4f, SliderFloatSettingEntryVE.NumberDisplayMode.Multiplier, "0.0",
                () => Settings.CursorScale.Value, v => Settings.CursorScale.Value = Mathf.Round(v * 10f) / 10f);
            AddToggle(rows, refreshers, "KEEP HORIZON LEVEL (UUVR)",
                "A UUVR setting. On: the game only turns you left and right, and you look up and down with your head, which some people find more comfortable. " +
                "Off (default): the mouse tilts the view up and down as usual.",
                () => UuvrCompat.GetSetting("AlignCameraToHorizon") == "True", on => UuvrCompat.SetSetting("AlignCameraToHorizon", on ? "True" : "False"));
            AddDropdown(rows, refreshers, "MONITOR WINDOW SHOWS",
                "What the game window on your monitor shows while you play in VR. Headset view with UI: what you see in the headset, menus included (default). " +
                "Headset view, no UI: Unity's plain mirror of one eye. UI only: just the menus and HUD on black.",
                new[] { "Headset view with UI", "Headset view, no UI", "UI only" },
                () => (int)Settings.DesktopView.Value, choice => Settings.DesktopView.Value = (DesktopViewMode)choice);
            AddSlider(rows, refreshers, "MONITOR WINDOW VIEW WIDTH",
                "How much of the headset view the monitor window shows when it shows the headset view with UI (default 90). Lower zooms in; higher shows more around the edges.",
                30f, 170f, SliderFloatSettingEntryVE.NumberDisplayMode.Value, Degrees,
                () => Settings.DesktopViewWidth.Value, v => Settings.DesktopViewWidth.Value = Mathf.Round(v));
            AddToggle(rows, refreshers, "ALLOW DLSS (EXPERIMENTAL, NEEDS RESTART)",
                "Puts DLSS back in Graphics > Upscaling. DLSS renders the game at a lower resolution and upscales it, which can raise the frame rate on NVIDIA RTX cards. " +
                "It's experimental in VR. Restart the game after turning this on, then choose DLSS under Graphics > Upscaling.",
                () => !Settings.DisableDlss.Value, on => Settings.DisableDlss.Value = !on);
            AddToggle(rows, refreshers, "TESTING: CONTROL FROM STEAMVR VR VIEW",
                "Only for testing without a headset, using SteamVR's null driver. While SteamVR's VR View window is the focused window, your keyboard and mouse are passed to the game. " +
                "Leave this off when you play with a real headset.",
                () => Settings.ControlFromVrView.Value, on => Settings.ControlFromVrView.Value = on);
        }

        private static void AddToggle(VisualElement rows, List<Action> refreshers, string label, string help, Func<bool> read, Action<bool> write)
        {
            var row = new BooleanSettingsEntryVE();
            var value = new IntSetting(read() ? 1 : 0, 0, 1, false);
            Prepare(row, label, help);
            row.Init(value);
            ShowToggleState(row, value.Value == 1);
            value.OnValueChanged += v => Apply(label, v == 1 ? "on" : "off", () => write(v == 1));
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

        private static void AddSlider(VisualElement rows, List<Action> refreshers, string label, string help, float min, float max,
            SliderFloatSettingEntryVE.NumberDisplayMode mode, string format, Func<float> read, Action<float> write)
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
            value.OnValueChanged += v => Apply(label, v.ToString(logFormat), () => write(v));
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
