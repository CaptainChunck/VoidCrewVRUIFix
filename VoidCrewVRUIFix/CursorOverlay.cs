using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// A software mouse cursor drawn as screen-space UI. Void Crew uses a Windows (hardware) cursor, which only
    /// exists on the desktop and never reaches the headset. Because this cursor is ordinary screen-space UI, the
    /// UI capture puts it on the VR panel exactly where the mouse points.
    /// </summary>
    internal sealed class CursorOverlay
    {
        private readonly Canvas _canvas;
        private readonly RawImage _image;
        private readonly RectTransform _rect;
        private Texture _texture;
        private bool _usingGameTexture;

        public CursorOverlay(Transform parent)
        {
            var root = new GameObject("VoidCrewVRUIFix_Cursor");
            root.transform.SetParent(parent, false);
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = short.MaxValue; // above every game canvas and UI Toolkit panel
            _canvas.enabled = false;

            var imageObject = new GameObject("Cursor", typeof(RectTransform));
            imageObject.transform.SetParent(root.transform, false);
            _image = imageObject.AddComponent<RawImage>();
            _image.raycastTarget = false;
            _rect = (RectTransform)imageObject.transform;
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.zero;
            _rect.pivot = new Vector2(0f, 1f); // the game's cursor hotspot is its top-left corner
        }

        public bool Visible { get { return _canvas.enabled; } }

        public string Describe()
        {
            return string.Format("visible {0}, texture {1}", Visible,
                _texture == null ? "none" : (_usingGameTexture ? "game cursor " : "built-in arrow ") + _texture.width + "x" + _texture.height);
        }

        public void Update(bool show)
        {
            Mouse mouse = Mouse.current;
            if (show && mouse != null)
            {
                EnsureTexture();
                float scale = Settings.CursorScale.Value;
                _rect.anchoredPosition = mouse.position.ReadValue();
                _rect.sizeDelta = new Vector2(_texture.width * scale, _texture.height * scale);
            }
            else
            {
                show = false;
            }

            if (_canvas.enabled != show) _canvas.enabled = show;
        }

        private void EnsureTexture()
        {
            if (_usingGameTexture && _texture != null) return;

            // CG.Input.MouseCursor keeps the game's cursor image in a private static field once input is initialised.
            var texture = GamePatches.GameCursorTexture;
            if (texture != null)
            {
                _texture = texture;
                _usingGameTexture = true;
                _image.texture = texture;
                Log.Verbose("VR cursor is using the game's cursor image (" + texture.width + "x" + texture.height + ").");
                return;
            }

            if (_texture == null)
            {
                _texture = CreateArrowTexture();
                _image.texture = _texture;
                Log.Verbose("Game cursor image not available yet; using the built-in arrow.");
            }
        }

        /// <summary>A plain white arrow with a black outline, used until the game's cursor image is available.</summary>
        private static Texture2D CreateArrowTexture()
        {
            const int size = 32;
            var outline = new[]
            {
                new Vector2(1, 1), new Vector2(1, 23), new Vector2(7, 17), new Vector2(11, 27),
                new Vector2(15, 25), new Vector2(11, 16), new Vector2(19, 16)
            };

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "VoidCrewVRUIFix Cursor",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Polygon coordinates have y pointing down; texture rows start at the bottom.
                    Color32 colour = new Color32(0, 0, 0, 0);
                    if (Inside(outline, x + 0.5f, y + 0.5f)) colour = new Color32(255, 255, 255, 255);
                    else if (NearInside(outline, x + 0.5f, y + 0.5f)) colour = new Color32(0, 0, 0, 255);
                    pixels[(size - 1 - y) * size + x] = colour;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static bool NearInside(Vector2[] polygon, float x, float y)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (Inside(polygon, x + dx, y + dy)) return true;
                }
            }
            return false;
        }

        private static bool Inside(Vector2[] polygon, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }
    }
}
