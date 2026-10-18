using System;
using System.Collections.Generic;
using UI.Core.Audio;
using UI.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// The "Modify UI" window, opened from the top of the VR tab of the game's Settings menu. It lists every piece of the
    /// VR UI that can be moved, draws where each one sits in your view, and has sliders for the chosen piece: left/right,
    /// up/down, forwards/backwards, size, and whether it follows your head. While it is open every head-locked piece shows
    /// in the headset (its last picture, or a labelled box), the chosen one solid and the others see-through, so the
    /// pieces can be lined up live and the window stays readable behind them.
    /// </summary>
    internal sealed class UiEditorWindow
    {
        // The picture of your view spans this much, in degrees across and up.
        private const float ViewAcross = 120f;
        private const float ViewUp = 70f;
        private const float RefreshInterval = 0.1f;

        private static readonly Color Background = new Color(0.035f, 0.06f, 0.08f, 0.97f);
        private static readonly Color ViewBackground = new Color(0.01f, 0.02f, 0.03f, 1f);
        private static readonly Color Accent = new Color(0.39f, 0.75f, 0.93f, 1f);
        private static readonly Color Gold = new Color(1f, 0.76f, 0.24f, 1f);
        private static readonly Color Text = new Color(0.93f, 0.95f, 0.97f, 1f);
        private static readonly Color Dim = new Color(0.62f, 0.68f, 0.74f, 1f);
        private static readonly Color Off = new Color(0.5f, 0.5f, 0.5f, 0.6f);

        private static readonly List<UiEditorWindow> Windows = new List<UiEditorWindow>();

        /// <summary>One entry in the list: the UI screen (no placement), a HUD piece, or the FPS counter.</summary>
        private sealed class Piece
        {
            public string Title;
            public string About;
            public LayerPlacement Placement;
            public HudLayer Layer;
            public bool IsFps;
            public Button ListButton;
            public VisualElement Box;
            public Label BoxLabel;
        }

        private readonly VisualElement _window;
        private readonly VisualElement _list;
        private readonly VisualElement _view;
        private readonly VisualElement _rows;
        private readonly Label _aboutTitle;
        private readonly Label _about;
        private readonly List<Piece> _pieces = new List<Piece>();
        private readonly List<Action> _refreshers = new List<Action>();
        private Piece _selected;
        private bool _open;
        private float _nextRefresh;

        /// <summary>True while a Modify UI window is open.</summary>
        public static bool AnyOpen
        {
            get
            {
                foreach (UiEditorWindow window in Windows)
                {
                    if (window._open) return true;
                }
                return false;
            }
        }

        /// <summary>Once per frame: closes windows whose menu was closed, and keeps the open window's picture current.</summary>
        public static void TickAll()
        {
            for (int i = Windows.Count - 1; i >= 0; i--)
            {
                UiEditorWindow window = Windows[i];
                if (window._window.panel == null)
                {
                    if (window._open) window.Close();
                    Windows.RemoveAt(i);
                    continue;
                }
                if (!window._open) continue;
                if (!VrSettingsTab.IsShowing(window._window))
                {
                    window.Close();
                    continue;
                }
                if (Time.unscaledTime < window._nextRefresh) continue;
                window._nextRefresh = Time.unscaledTime + RefreshInterval;
                window.UpdateView();
            }
        }

        /// <summary>The settings were all changed at once (RESTORE on the VR tab): open windows show the new values.</summary>
        public static void RefreshAll()
        {
            foreach (UiEditorWindow window in Windows)
            {
                if (!window._open) continue;
                foreach (Action refresh in window._refreshers) refresh();
                window.UpdateView();
            }
        }

        /// <summary>A Settings menu just opened: it starts on its tabs, not on a window left open last time.</summary>
        public static void CloseAll()
        {
            foreach (UiEditorWindow window in Windows)
            {
                if (window._open) window.Close();
            }
        }

        /// <summary>Builds the window (hidden) over the given part of the Settings menu.</summary>
        public UiEditorWindow(VisualElement host)
        {
            _window = new VisualElement { name = "VoidCrewVRUIFix_ModifyUiWindow" };
            IStyle style = _window.style;
            style.position = Position.Absolute;
            style.left = Length.Percent(1.5f);
            style.right = Length.Percent(1.5f);
            style.top = Length.Percent(1.5f);
            style.bottom = Length.Percent(1.5f);
            style.flexDirection = FlexDirection.Column;
            style.backgroundColor = Background;
            style.display = DisplayStyle.None;
            SetBorder(_window, 2f, new Color(Accent.r, Accent.g, Accent.b, 0.7f));
            SetPadding(_window, 18f);

            // Title row.
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6f;
            var title = new Label("MODIFY VR UI");
            title.style.fontSize = 34f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Text;
            title.style.flexGrow = 1f;
            header.Add(title);
            header.Add(MakeButton("DONE", Close, true, 170f));
            _window.Add(header);

            var intro = new Label("Choose a piece of the UI, then move it with the sliders. While this window is open, every piece shows in the headset: " +
                                  "the chosen one solid, the others see-through. Left/right and up/down are angles of your view; forwards/backwards is the distance " +
                                  "from your eyes, and pieces at the same distance sit on the same plane.");
            intro.style.fontSize = 17f;
            intro.style.color = Dim;
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginBottom = 12f;
            _window.Add(intro);

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;
            body.style.minHeight = 0f;
            _window.Add(body);

            // The pieces.
            _list = new VisualElement();
            _list.style.width = Length.Percent(22f);
            _list.style.flexShrink = 0f;
            _list.style.marginRight = 16f;
            body.Add(_list);

            // The picture of your view, then the chosen piece's sliders.
            var right = new VisualElement();
            right.style.flexGrow = 1f;
            right.style.flexDirection = FlexDirection.Column;
            right.style.minWidth = 0f;
            body.Add(right);

            var viewCaption = new Label("YOUR VIEW IN THE HEADSET (" + ViewAcross.ToString("0") + "° ACROSS) - CLICK A BOX TO CHOOSE IT");
            viewCaption.style.fontSize = 15f;
            viewCaption.style.color = Dim;
            viewCaption.style.marginBottom = 4f;
            right.Add(viewCaption);

            _view = new VisualElement();
            _view.style.height = Length.Percent(44f);
            _view.style.minHeight = 260f;
            _view.style.flexShrink = 0f;
            _view.style.backgroundColor = ViewBackground;
            _view.style.overflow = Overflow.Hidden;
            SetBorder(_view, 1f, new Color(0.3f, 0.42f, 0.5f, 1f));
            right.Add(_view);
            AddGuides(_view);

            _aboutTitle = new Label();
            _aboutTitle.style.fontSize = 24f;
            _aboutTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _aboutTitle.style.color = Gold;
            _aboutTitle.style.marginTop = 10f;
            right.Add(_aboutTitle);
            _about = new Label();
            _about.style.fontSize = 16f;
            _about.style.color = Dim;
            _about.style.whiteSpace = WhiteSpace.Normal;
            _about.style.marginBottom = 6f;
            right.Add(_about);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            right.Add(scroll);
            _rows = scroll.contentContainer;

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.FlexEnd;
            footer.style.marginTop = 8f;
            footer.Add(MakeButton("RESET THIS PIECE", ResetSelected, false, 260f));
            right.Add(footer);

            AddPieces();
            host.Add(_window);
            Windows.Add(this);
        }

        public void Open()
        {
            _open = true;
            LayerPlacement.Editing = true;
            _window.style.display = DisplayStyle.Flex;
            _window.BringToFront();
            Select(_selected ?? _pieces[0]);
            Log.Verbose("Modify UI window opened.");
        }

        public void Close()
        {
            _open = false;
            _window.style.display = DisplayStyle.None;
            if (!AnyOpen)
            {
                LayerPlacement.Editing = false;
                LayerPlacement.Selected = null;
            }
        }

        private void AddPieces()
        {
            AddPiece(new Piece
            {
                Title = "UI SCREEN",
                About = "The floating screen with the menus, markers and the rest of the HUD: how big it looks, how far away it is, how high it sits, " +
                        "and whether it follows your head."
            });
            foreach (HudLayer layer in HudLayers.All)
            {
                AddPiece(new Piece { Title = layer.Placement.Title, About = layer.Placement.Description, Placement = layer.Placement, Layer = layer });
            }
            AddPiece(new Piece { Title = Settings.FpsPlacement.Title, About = "The frame-rate counter, always locked to your head.", Placement = Settings.FpsPlacement, IsFps = true });
        }

        private void AddPiece(Piece piece)
        {
            piece.ListButton = MakeButton(piece.Title, () => Select(piece), false, -1f);
            piece.ListButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            piece.ListButton.style.marginBottom = 6f;
            _list.Add(piece.ListButton);

            piece.Box = new VisualElement();
            piece.Box.style.position = Position.Absolute;
            piece.Box.style.justifyContent = Justify.Center;
            piece.BoxLabel = new Label(piece.Title) { pickingMode = PickingMode.Ignore };
            piece.BoxLabel.style.fontSize = 13f;
            piece.BoxLabel.style.color = Text;
            piece.BoxLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            piece.BoxLabel.style.whiteSpace = WhiteSpace.Normal;
            piece.Box.Add(piece.BoxLabel);
            piece.Box.RegisterCallback<ClickEvent>(evt => Select(piece));
            _view.Add(piece.Box);
            _pieces.Add(piece);
        }

        private void Select(Piece piece)
        {
            _selected = piece;
            LayerPlacement.Selected = piece.Placement;
            foreach (Piece other in _pieces)
            {
                bool chosen = other == piece;
                other.ListButton.style.backgroundColor = chosen ? new Color(0.55f, 0.4f, 0.1f, 0.9f) : new Color(0.08f, 0.13f, 0.17f, 1f);
                SetBorder(other.ListButton, chosen ? 2f : 1f, chosen ? Gold : new Color(0.25f, 0.38f, 0.46f, 1f));
            }
            _aboutTitle.text = piece.Title;
            _about.text = piece.About;
            BuildRows(piece);
            UpdateView();
        }

        /// <summary>The chosen piece's rows, made from the game's own setting rows like the VR tab's.</summary>
        private void BuildRows(Piece piece)
        {
            _rows.Clear();
            _refreshers.Clear();
            string name = piece.Title + " ";
            if (piece.Placement == null)
            {
                VrSettingsTab.AddSlider(_rows, _refreshers, "SIZE", "How wide the floating UI screen looks, as an angle of your view (default " +
                    ((float)Settings.PanelWidthDegrees.DefaultValue).ToString("0") + "). Bigger is easier to read; " +
                    "smaller keeps the edges of menus in sight.", 30f, 120f, SliderFloatSettingEntryVE.NumberDisplayMode.Value, VrSettingsTab.Degrees,
                    () => Settings.PanelWidthDegrees.Value, v => Settings.PanelWidthDegrees.Value = Mathf.Round(v), name + "SIZE");
                VrSettingsTab.AddSlider(_rows, _refreshers, "FORWARDS / BACKWARDS", "How far away the UI screen floats (default " +
                    ((float)Settings.PanelDistance.DefaultValue).ToString("0.0") + " m). It looks the same size at any distance, " +
                    "so this only changes how deep it sits compared with the game world.", 0.5f, 5f, SliderFloatSettingEntryVE.NumberDisplayMode.Value, "0.0 m",
                    () => Settings.PanelDistance.Value, v => Settings.PanelDistance.Value = Mathf.Round(v * 10f) / 10f, name + "DISTANCE");
                VrSettingsTab.AddSlider(_rows, _refreshers, "UP / DOWN", "Moves the UI screen up or down, in metres (default " +
                    ((float)Settings.PanelHeightOffset.DefaultValue).ToString("0.00") + " m; 0 is centred on your view).",
                    -1f, 1f, SliderFloatSettingEntryVE.NumberDisplayMode.Value, "0.00 m",
                    () => Settings.PanelHeightOffset.Value, v => Settings.PanelHeightOffset.Value = Mathf.Round(v * 20f) / 20f, name + "HEIGHT");
                VrSettingsTab.AddToggle(_rows, _refreshers, "FOLLOWS YOUR HEAD", "On: the UI screen stays in front of your eyes wherever you look. Off (default): it stays in " +
                    "front of the game camera, so you can look around it, and markers and pings line up with the world. With Aim With Your Head it follows your head while you play.",
                    () => Settings.Anchor.Value == PanelAnchor.Head, on => Settings.Anchor.Value = on ? PanelAnchor.Head : PanelAnchor.GameCamera, name + "FOLLOWS YOUR HEAD");
                return;
            }

            LayerPlacement placement = piece.Placement;
            if (piece.IsFps)
            {
                VrSettingsTab.AddToggle(_rows, _refreshers, "SHOW FPS COUNTER", "Shows the game's frame rate in the headset (default off). Green: the headset's full refresh rate. " +
                    "Yellow: half of it or more. Red: lower than that.", () => Settings.ShowFpsCounter.Value, on => Settings.ShowFpsCounter.Value = on, name + "ON");
            }
            else
            {
                VrSettingsTab.AddToggle(_rows, _refreshers, "FOLLOWS YOUR HEAD", "On (default): this piece is locked to your head, so it stays in view wherever you look. " +
                    "Off: it stays on the UI screen where the game draws it.", () => placement.FollowsHead.Value, on => placement.FollowsHead.Value = on, name + "FOLLOWS YOUR HEAD");
            }
            VrSettingsTab.AddSlider(_rows, _refreshers, "LEFT / RIGHT", "Moves it left (negative) or right (positive) in your view, in degrees.",
                -60f, 60f, SliderFloatSettingEntryVE.NumberDisplayMode.Value, VrSettingsTab.Degrees,
                () => placement.LeftRight.Value, v => placement.LeftRight.Value = Mathf.Round(v), name + "LEFT / RIGHT");
            VrSettingsTab.AddSlider(_rows, _refreshers, "UP / DOWN", "Moves it down (negative) or up (positive) in your view, in degrees.",
                -60f, 60f, SliderFloatSettingEntryVE.NumberDisplayMode.Value, VrSettingsTab.Degrees,
                () => placement.UpDown.Value, v => placement.UpDown.Value = Mathf.Round(v), name + "UP / DOWN");
            VrSettingsTab.AddSlider(_rows, _refreshers, "FORWARDS / BACKWARDS", "How far in front of your eyes it floats, in metres. It is always kept nearer than the UI " +
                "screen, so it is never hidden behind it; give pieces the same distance to put them on the same plane.",
                0.3f, 3f, SliderFloatSettingEntryVE.NumberDisplayMode.Value, "0.0 m",
                () => placement.Distance.Value, v => placement.Distance.Value = Mathf.Round(v * 10f) / 10f, name + "DISTANCE");
            VrSettingsTab.AddSlider(_rows, _refreshers, "SIZE", "Its size (1.0X is as big as it looks on the UI screen).",
                0.5f, 2f, SliderFloatSettingEntryVE.NumberDisplayMode.Multiplier, "0.0",
                () => placement.Size.Value, v => placement.Size.Value = Mathf.Round(v * 10f) / 10f, name + "SIZE");
        }

        private void ResetSelected()
        {
            if (_selected == null) return;
            if (_selected.Placement != null)
            {
                _selected.Placement.ResetToDefaults();
            }
            else
            {
                Settings.PanelWidthDegrees.Value = (float)Settings.PanelWidthDegrees.DefaultValue;
                Settings.PanelDistance.Value = (float)Settings.PanelDistance.DefaultValue;
                Settings.PanelHeightOffset.Value = (float)Settings.PanelHeightOffset.DefaultValue;
                Settings.Anchor.Value = (PanelAnchor)Settings.Anchor.DefaultValue;
            }
            foreach (Action refresh in _refreshers) refresh();
            UpdateView();
            Log.Info("Modify UI: " + _selected.Title.ToLowerInvariant() + " reset to its defaults.");
        }

        /// <summary>Moves the boxes to where each piece sits now.</summary>
        private void UpdateView()
        {
            foreach (Piece piece in _pieces)
            {
                bool chosen = piece == _selected;
                if (piece.Placement == null)
                {
                    float across = Mathf.Clamp(Settings.PanelWidthDegrees.Value, 20f, 140f);
                    float up = 2f * Mathf.Atan(Mathf.Tan(across * 0.5f * Mathf.Deg2Rad) / PanelLayout.Aspect) * Mathf.Rad2Deg;
                    float pitch = Mathf.Atan2(Settings.PanelHeightOffset.Value, Mathf.Clamp(Settings.PanelDistance.Value, 0.3f, 10f)) * Mathf.Rad2Deg;
                    Place(piece.Box, 0f, pitch, across, up);
                    StyleBox(piece, chosen, true, new Color(0.75f, 0.78f, 0.8f, 0.7f));
                    piece.BoxLabel.text = "UI SCREEN";
                    piece.BoxLabel.style.unityTextAlign = TextAnchor.UpperLeft;
                    if (!chosen) piece.Box.SendToBack();
                    continue;
                }

                LayerPlacement placement = piece.Placement;
                bool on = piece.IsFps ? Settings.ShowFpsCounter.Value : placement.Enabled;
                Vector2 size = piece.IsFps ? FpsAngularSize(placement) : piece.Layer.AngularSize();
                Place(piece.Box, placement.LeftRight.Value, placement.UpDown.Value, size.x, size.y);
                StyleBox(piece, chosen, on, Accent);
                piece.BoxLabel.text = piece.Title + "\n" + (on ? placement.EffectiveDistance().ToString("0.00") + " m" : piece.IsFps ? "(OFF)" : "(ON UI SCREEN)");
                piece.ListButton.text = piece.Title + (on ? "" : piece.IsFps ? "  (OFF)" : "  (ON UI SCREEN)");
                if (chosen) piece.Box.BringToFront();
            }
        }

        /// <summary>The FPS counter's size in degrees: its width one metre away, times its size setting.</summary>
        private static Vector2 FpsAngularSize(LayerPlacement placement)
        {
            float across = 2f * Mathf.Atan(FpsCounter.WidthAtOneMetre * placement.Scale * 0.5f) * Mathf.Rad2Deg;
            return new Vector2(across, across * FpsCounter.Aspect);
        }

        /// <summary>Puts a box where something centred at (yaw, pitch) degrees and this many degrees big sits in the picture.</summary>
        private static void Place(VisualElement box, float yaw, float pitch, float across, float up)
        {
            box.style.left = Length.Percent((yaw - across * 0.5f) / ViewAcross * 100f + 50f);
            box.style.top = Length.Percent(50f - (pitch + up * 0.5f) / ViewUp * 100f);
            box.style.width = Length.Percent(Mathf.Max(across, 1f) / ViewAcross * 100f);
            box.style.height = Length.Percent(Mathf.Max(up, 1f) / ViewUp * 100f);
        }

        private static void StyleBox(Piece piece, bool chosen, bool on, Color colour)
        {
            Color edge = chosen ? Gold : on ? colour : Off;
            SetBorder(piece.Box, chosen ? 3f : 2f, edge);
            piece.Box.style.backgroundColor = new Color(edge.r, edge.g, edge.b, chosen ? 0.22f : on ? 0.1f : 0.04f);
            piece.BoxLabel.style.color = chosen ? Gold : on ? Text : Dim;
        }

        /// <summary>The middle of your view and a faint cross through it.</summary>
        private static void AddGuides(VisualElement view)
        {
            var across = new VisualElement { pickingMode = PickingMode.Ignore };
            across.style.position = Position.Absolute;
            across.style.left = 0f;
            across.style.right = 0f;
            across.style.top = Length.Percent(50f);
            across.style.height = 1f;
            across.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
            view.Add(across);

            var up = new VisualElement { pickingMode = PickingMode.Ignore };
            up.style.position = Position.Absolute;
            up.style.top = 0f;
            up.style.bottom = 0f;
            up.style.left = Length.Percent(50f);
            up.style.width = 1f;
            up.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
            view.Add(up);

            var middle = new Label("+") { pickingMode = PickingMode.Ignore };
            middle.style.position = Position.Absolute;
            middle.style.left = Length.Percent(50f);
            middle.style.top = Length.Percent(50f);
            middle.style.marginLeft = -6f;
            middle.style.marginTop = -11f;
            middle.style.fontSize = 18f;
            middle.style.color = new Color(1f, 1f, 1f, 0.5f);
            view.Add(middle);
        }

        private static Button MakeButton(string text, Action clicked, bool primary, float width)
        {
            var button = new Button(clicked) { text = text };
            IStyle style = button.style;
            style.height = 46f;
            if (width > 0f) style.width = width;
            style.fontSize = 19f;
            style.unityFontStyleAndWeight = FontStyle.Bold;
            style.color = Text;
            style.backgroundColor = primary ? new Color(0.12f, 0.42f, 0.6f, 1f) : new Color(0.08f, 0.13f, 0.17f, 1f);
            style.unityTextAlign = TextAnchor.MiddleCenter;
            style.marginLeft = 0f;
            style.marginRight = 0f;
            SetBorder(button, primary ? 2f : 1f, primary ? Accent : new Color(0.25f, 0.38f, 0.46f, 1f));
            SetPadding(button, 6f);
            UIAudioProvider.AddSound(button, UIAudioEventRef.ESoundType.Click, "list");
            UIAudioProvider.AddSound(button, UIAudioEventRef.ESoundType.Hover, "list");
            return button;
        }

        private static void SetBorder(VisualElement element, float width, Color colour)
        {
            IStyle style = element.style;
            style.borderLeftWidth = width;
            style.borderRightWidth = width;
            style.borderTopWidth = width;
            style.borderBottomWidth = width;
            style.borderLeftColor = colour;
            style.borderRightColor = colour;
            style.borderTopColor = colour;
            style.borderBottomColor = colour;
        }

        private static void SetPadding(VisualElement element, float padding)
        {
            IStyle style = element.style;
            style.paddingLeft = padding;
            style.paddingRight = padding;
            style.paddingTop = padding;
            style.paddingBottom = padding;
        }
    }
}
