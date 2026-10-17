using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// While a wheel (pings, emotes) is held open, the game dims the whole screen behind it with a dark layer. On a
    /// monitor that dims the world; in VR it made the whole UI screen a dark box, and the head-locked HUDs, which are
    /// cut out of the same picture, dark boxes too. A few frames after a wheel opens (once it has been laid out), every
    /// element on the wheels' panel that covers the whole screen has its background cleared. The wheel itself, its
    /// options and the dark diamond behind them stay. The backgrounds come back when VR is turned off.
    /// </summary>
    internal static class WheelBackdrop
    {
        private const int ScanFrames = 10;
        private const float ScreenShare = 0.9f;

        private static readonly List<VisualElement> Cleared = new List<VisualElement>();
        private static VisualElement _shown;
        private static int _scanUntilFrame;
        private static bool _broken;

        public static string Describe()
        {
            if (!Settings.HideWheelBackdrop.Value) return "shown (game default)";
            if (_broken) return "could not be hidden (see the log)";
            return Cleared.Count > 0 ? "hidden in VR (" + Cleared.Count + " full-screen layer(s))" : "hidden in VR (no wheel opened yet)";
        }

        /// <summary>A wheel was just shown; its panel is looked at over the next few frames, once it has been laid out.</summary>
        public static void Shown(VisualElement wheel)
        {
            _shown = wheel;
            _scanUntilFrame = Time.frameCount + ScanFrames;
        }

        /// <summary>Once per frame while VR is on.</summary>
        public static void Update()
        {
            if (_shown == null || _broken || Time.frameCount > _scanUntilFrame || !Settings.HideWheelBackdrop.Value) return;
            try
            {
                IPanel panel = _shown.panel;
                if (panel == null) return;
                VisualElement root = panel.visualTree;
                Rect screen = root.worldBound;
                if (screen.width < 1f || screen.height < 1f) return;
                Clear(root, screen);
            }
            catch (Exception e)
            {
                _broken = true;
                Log.Failure("Hiding the dark layer behind the ping wheel", e);
            }
        }

        /// <summary>VR stopped, or the setting was turned off: the game's own backgrounds come back.</summary>
        public static void Restore()
        {
            foreach (VisualElement element in Cleared)
            {
                if (element == null) continue;
                element.style.backgroundColor = StyleKeyword.Null;
                element.style.backgroundImage = StyleKeyword.Null;
            }
            if (Cleared.Count > 0) Log.Verbose("The wheels' dark backgrounds are shown again.");
            Cleared.Clear();
            _shown = null;
        }

        private static void Clear(VisualElement element, Rect screen)
        {
            IResolvedStyle style = element.resolvedStyle;
            if (style.display == DisplayStyle.None) return;

            Rect box = element.worldBound;
            if (box.width >= screen.width * ScreenShare && box.height >= screen.height * ScreenShare && !Cleared.Contains(element))
            {
                bool colour = style.backgroundColor.a > 0.01f;
                Background image = style.backgroundImage;
                bool picture = image.texture != null || image.sprite != null || image.vectorImage != null || image.renderTexture != null;
                if (colour || picture)
                {
                    if (colour) element.style.backgroundColor = new StyleColor(Color.clear);
                    if (picture) element.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                    Cleared.Add(element);
                    Log.InfoOnce("wheel-backdrop", "The dark layer the game puts behind the ping wheel is hidden in VR (it showed as dark boxes around the UI).");
                    Log.Verbose("Cleared the full-screen background of wheel element '" + element.name + "' (" + element.GetType().Name + ").");
                }
            }

            for (int i = 0; i < element.hierarchy.childCount; i++) Clear(element.hierarchy[i], screen);
        }
    }
}
