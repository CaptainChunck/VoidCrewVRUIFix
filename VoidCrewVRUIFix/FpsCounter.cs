using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// A small frame-rate counter for the headset, by default in the top left of your view (the FPS Counter placement
    /// settings move it): the game's frames per second, green at the headset's full refresh rate, yellow at half of it
    /// or more, red below that. It is drawn with a tiny built-in pixel font (PixelFont) into its own texture, so it needs
    /// nothing from the game's UI, and the displays show it locked to your view.
    /// </summary>
    internal sealed class FpsCounter
    {
        /// <summary>Width of the counter one metre away, at size 1.</summary>
        public const float WidthAtOneMetre = 0.13f;

        private const float RefreshInterval = 0.5f;
        private const int MaxCharacters = 7;
        private const int Scale = 4;
        private const int Padding = 6;
        private const int TextureWidth = Padding * 2 + (MaxCharacters * PixelFont.Advance - 1) * Scale;
        private const int TextureHeight = Padding * 2 + PixelFont.GlyphHeight * Scale;

        private static readonly Color32 Background = new Color32(0, 0, 0, 150);
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        private static readonly Color32 Green = new Color32(90, 230, 90, 255);
        private static readonly Color32 Yellow = new Color32(255, 210, 60, 255);
        private static readonly Color32 Red = new Color32(255, 80, 70, 255);
        private static readonly Color32 White = new Color32(240, 240, 240, 255);

        private static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();

        private readonly Color32[] _pixels = new Color32[TextureWidth * TextureHeight];
        private Texture2D _texture;
        private bool _shown;
        private int _frames;
        private float _elapsed;
        private string _drawnText;
        private Color32 _drawnColour;

        /// <summary>The counter's picture while it is turned on, otherwise null.</summary>
        public Texture Texture { get { return _shown ? _texture : null; } }

        /// <summary>Height divided by width, for sizing the counter in the headset.</summary>
        public static float Aspect { get { return (float)TextureHeight / TextureWidth; } }

        /// <summary>The last frame rate shown, for the diagnostics report.</summary>
        public int LastFps { get; private set; }

        /// <summary>The counter where its placement settings put it, locked to the head; its texture is null while it is off.</summary>
        public HeadLockedPicture Picture()
        {
            LayerPlacement placement = Settings.FpsPlacement;
            var picture = new HeadLockedPicture { Texture = Texture, Alpha = placement.Alpha };
            placement.Place(out picture.Position, out picture.Rotation);
            float width = WidthAtOneMetre * placement.EffectiveDistance() * placement.Scale;
            picture.Size = new Vector2(width, width * Aspect);
            return picture;
        }

        /// <summary>Once per frame while VR is on.</summary>
        public void Update(bool enabled)
        {
            if (!enabled)
            {
                _shown = false;
                _frames = 0;
                _elapsed = 0f;
                return;
            }

            if (_texture == null)
            {
                _texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false, false)
                {
                    name = "VoidCrewVRUIFix FPS counter",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                Draw("--", White);
            }

            _shown = true;
            _frames++;
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed < RefreshInterval) return;

            int fps = Mathf.Clamp(Mathf.RoundToInt(_frames / _elapsed), 0, 999);
            _frames = 0;
            _elapsed = 0f;
            LastFps = fps;
            Draw(fps.ToString(), ColourFor(fps));
        }

        public void Release()
        {
            if (_texture != null) Object.Destroy(_texture);
            _texture = null;
            _shown = false;
        }

        private static Color32 ColourFor(int fps)
        {
            float refreshRate = HeadsetRefreshRate();
            if (refreshRate <= 0f) return White;
            if (fps >= 0.9f * refreshRate) return Green;
            if (fps >= 0.45f * refreshRate) return Yellow;
            return Red;
        }

        private static float HeadsetRefreshRate()
        {
            Displays.Clear();
            SubsystemManager.GetInstances(Displays);
            foreach (XRDisplaySubsystem display in Displays)
            {
                float rate;
                if (display.running && display.TryGetDisplayRefreshRate(out rate) && rate > 0f) return rate;
            }
            return 0f;
        }

        /// <summary>Draws "&lt;number&gt; FPS" centred on a dark background. Rows are stored bottom first, Unity's convention.</summary>
        private void Draw(string number, Color32 colour)
        {
            string text = number + " FPS";
            if (text == _drawnText && colour.Equals(_drawnColour)) return;
            _drawnText = text;
            _drawnColour = colour;

            int textWidth = PixelFont.Width(text, Scale);
            int left = (TextureWidth - textWidth) / 2;
            int backgroundLeft = Mathf.Max(0, left - Padding);
            int backgroundRight = Mathf.Min(TextureWidth, left + textWidth + Padding);
            for (int y = 0; y < TextureHeight; y++)
            {
                for (int x = 0; x < TextureWidth; x++)
                {
                    _pixels[y * TextureWidth + x] = x >= backgroundLeft && x < backgroundRight ? Background : Clear;
                }
            }
            PixelFont.Draw(_pixels, TextureWidth, TextureHeight, text, left, Padding, Scale, colour);

            _texture.SetPixels32(_pixels);
            _texture.Apply(false, false);
        }
    }
}
