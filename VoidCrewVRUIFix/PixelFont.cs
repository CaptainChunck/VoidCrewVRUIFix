using System.Collections.Generic;
using UnityEngine;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// A tiny 5 x 7 pixel font for the plugin's own labels in the headset (the FPS counter and the HUD placeholder),
    /// so they need no fonts or UI from the game. Digits, capital letters and '-'; anything else is left as a gap.
    /// </summary>
    internal static class PixelFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;
        public const int Advance = 6;

        // Top row first; bit 4 is the leftmost column.
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
            { 'A', new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11 } },
            { 'B', new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E } },
            { 'C', new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E } },
            { 'D', new byte[] { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E } },
            { 'E', new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F } },
            { 'F', new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 } },
            { 'G', new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F } },
            { 'H', new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 } },
            { 'I', new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E } },
            { 'J', new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C } },
            { 'K', new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 } },
            { 'L', new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F } },
            { 'M', new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 } },
            { 'N', new byte[] { 0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11 } },
            { 'O', new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E } },
            { 'P', new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 } },
            { 'Q', new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D } },
            { 'R', new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 } },
            { 'S', new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E } },
            { 'T', new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 } },
            { 'U', new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E } },
            { 'V', new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 } },
            { 'W', new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A } },
            { 'X', new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 } },
            { 'Y', new byte[] { 0x11, 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04 } },
            { 'Z', new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F } },
            { '-', new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 } }
        };

        /// <summary>Width in pixels of the text drawn at this scale.</summary>
        public static int Width(string text, int scale)
        {
            return text.Length == 0 ? 0 : (text.Length * Advance - 1) * scale;
        }

        /// <summary>
        /// Draws text into a pixel buffer whose rows are stored bottom first (Unity's convention). <paramref name="top"/>
        /// is measured down from the top edge. Pixels outside the buffer are skipped.
        /// </summary>
        public static void Draw(Color32[] pixels, int width, int height, string text, int left, int top, int scale, Color32 colour)
        {
            for (int i = 0; i < text.Length; i++)
            {
                byte[] glyph;
                if (!Glyphs.TryGetValue(text[i], out glyph)) continue;
                for (int row = 0; row < GlyphHeight; row++)
                {
                    for (int column = 0; column < GlyphWidth; column++)
                    {
                        if ((glyph[row] & (0x10 >> column)) == 0) continue;
                        int x0 = left + (i * Advance + column) * scale;
                        int y0 = top + row * scale;
                        for (int dy = 0; dy < scale; dy++)
                        {
                            int y = height - 1 - (y0 + dy);
                            if (y < 0 || y >= height) continue;
                            for (int dx = 0; dx < scale; dx++)
                            {
                                int x = x0 + dx;
                                if (x >= 0 && x < width) pixels[y * width + x] = colour;
                            }
                        }
                    }
                }
            }
        }
    }
}
