using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// A small frame-rate counter for the headset, in the top left of your view: the game's frames per second, green at
    /// the headset's full refresh rate, yellow at half of it or more, red below that. It is drawn with a tiny built-in
    /// pixel font into its own texture, so it needs nothing from the game's UI, and the displays show it locked to your
    /// view.
    /// </summary>
    internal sealed class FpsCounter
    {
        /// <summary>Position relative to your eyes, for a counter one metre away: up and to the left.</summary>
        public static readonly Vector2 OffsetAtOneMetre = new Vector2(-0.27f, 0.21f);

        /// <summary>Width of the counter one metre away.</summary>
        public const float WidthAtOneMetre = 0.13f;

        private const float RefreshInterval = 0.5f;
        private const int MaxCharacters = 7;
        private const int GlyphWidth = 5;
        private const int GlyphHeight = 7;
        private const int Advance = 6;
        private const int Scale = 4;
        private const int Padding = 6;
        private const int TextureWidth = Padding * 2 + (MaxCharacters * Advance - 1) * Scale;
        private const int TextureHeight = Padding * 2 + GlyphHeight * Scale;

        private static readonly Color32 Background = new Color32(0, 0, 0, 150);
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        private static readonly Color32 Green = new Color32(90, 230, 90, 255);
        private static readonly Color32 Yellow = new Color32(255, 210, 60, 255);
        private static readonly Color32 Red = new Color32(255, 80, 70, 255);
        private static readonly Color32 White = new Color32(240, 240, 240, 255);

        // 5 x 7 pixel glyphs, top row first; bit 4 is the leftmost column.
        private static readonly Dictionary<char, byte[]> Glyphs = new Dictionary<char, byte[]>
        {
            { '0', new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E } },
            { '1', new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E } },
            { '2', new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F } },
            { '3', new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E } },
            { '4', new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 } },
            { '5', new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E } },
            { '6', new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E } },
            { '7', new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 } },
            { '8', new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E } },
            { '9', new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C } },
            { 'F', new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 } },
            { 'P', new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 } },
            { 'S', new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E } },
            { '-', new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 } }
        };

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

        /// <summary>How far in front of your eyes: one metre, or nearer than a close UI screen so it is never behind it.</summary>
        public static float Distance(float panelDistance)
        {
            return Mathf.Min(1f, 0.8f * panelDistance);
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

            int textWidth = (text.Length * Advance - 1) * Scale;
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

            for (int i = 0; i < text.Length; i++)
            {
                byte[] glyph;
                if (!Glyphs.TryGetValue(text[i], out glyph)) continue;
                for (int row = 0; row < GlyphHeight; row++)
                {
                    for (int column = 0; column < GlyphWidth; column++)
                    {
                        if ((glyph[row] & (0x10 >> column)) == 0) continue;
                        int x0 = left + (i * Advance + column) * Scale;
                        int top = Padding + row * Scale;
                        for (int dy = 0; dy < Scale; dy++)
                        {
                            int y = TextureHeight - 1 - (top + dy);
                            for (int dx = 0; dx < Scale; dx++) _pixels[y * TextureWidth + x0 + dx] = colour;
                        }
                    }
                }
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false, false);
        }
    }
}
