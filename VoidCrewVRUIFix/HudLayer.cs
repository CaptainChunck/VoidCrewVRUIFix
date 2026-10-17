using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// The player status HUD (portrait, health, oxygen, temperature and its key hints) as its own layer locked to your
    /// head, so it stays in view wherever you look, at a place you choose in the VR tab. In each gameplay frame the HUD's
    /// rectangle is copied out of the captured UI into its own texture and cleared from the UI screen. The crosshair,
    /// markers and prompts stay on the UI screen, lined up with where you aim. While a HUD setting is being changed, the
    /// last copy (or a "HUD" placeholder) is shown even with menus open, so you can see where it goes.
    /// </summary>
    internal sealed class HudLayer
    {
        private const float FindInterval = 1f;
        private const float PreviewSeconds = 6f;
        private const int Grid = 16;
        private const int Padding = 6;
        private const float MaxShareOfScreen = 0.6f;

        // The status HUD's usual size as a share of the screen, used for the placeholder.
        private const float PlaceholderWidthShare = 0.27f;
        private const float PlaceholderHeightShare = 0.2f;

        // The game's status HUD (UI.MainHUD.PlayerHUD) draws these containers of its UI document.
        private static readonly string[] HudElementNames = { "LocalPlayerContainer", "KeybindsContainer" };

        private readonly List<VisualElement> _hudElements = new List<VisualElement>();
        private Type _playerHudType;
        private bool _typeLookupDone;
        private Component _playerHud;
        private VisualElement _cachedRoot;
        private float _nextFind;

        private RectInt _pixelRect;
        private bool _extract;
        private RenderTexture _texture;
        private bool _hasCopy;
        private Texture2D _placeholder;
        private float _previewUntil = -1f;
        private Material _clearMaterial;
        private bool _clearLookupDone;
        private Mesh _quad;

        /// <summary>True when the HUD is copied out of this frame's UI picture.</summary>
        public bool Extracting { get { return _extract; } }

        /// <summary>The copied rectangle as a share of the UI picture (origin bottom left).</summary>
        public Rect UvRect
        {
            get
            {
                float width = Mathf.Max(1, Screen.width);
                float height = Mathf.Max(1, Screen.height);
                return new Rect(_pixelRect.x / width, _pixelRect.y / height, _pixelRect.width / width, _pixelRect.height / height);
            }
        }

        public Texture CopyTexture { get { return _texture; } }

        public bool Previewing { get { return Time.unscaledTime < _previewUntil; } }

        /// <summary>The picture to show locked to the head right now, or null for none.</summary>
        public Texture DisplayTexture
        {
            get
            {
                if (!Settings.HudFollowsHead.Value) return null;
                if (_extract) return _texture;
                if (!Previewing) return null;
                return _hasCopy && _texture != null ? (Texture)_texture : Placeholder();
            }
        }

        /// <summary>Shows the HUD for a few seconds, even with menus open, while its settings are being changed.</summary>
        public void StartPreview()
        {
            _previewUntil = Time.unscaledTime + PreviewSeconds;
        }

        /// <summary>VR stopped: nothing is copied until it starts again.</summary>
        public void Stop()
        {
            _extract = false;
        }

        public string Describe()
        {
            if (!Settings.HudFollowsHead.Value) return "off (the status HUD stays on the UI screen)";
            string found = _playerHud != null ? "found" : (_playerHudType == null && _typeLookupDone ? "the game's HUD type was not found" : "not on screen");
            return found + (_extract ? ", copied this frame from " + _pixelRect.width + "x" + _pixelRect.height + " at " + _pixelRect.x + "," + _pixelRect.y : "") +
                   (Previewing ? ", previewing" : "") +
                   string.Format(" | {0:0}° left/right, {1:0}° up/down, {2:0.00} m away, size x{3:0.0}",
                       Settings.HudLeftRight.Value, Settings.HudUpDown.Value, Distance(), Settings.HudSize.Value);
        }

        /// <summary>
        /// Main thread, once per frame while VR is on: finds the HUD and decides whether to copy it this frame. Not while
        /// a menu or terminal is open, or a full-screen layer covers the screen, since the HUD's rectangle then shows
        /// something else.
        /// </summary>
        public void Update(bool menuOpen, bool fullScreenLayer)
        {
            _extract = false;
            if (!Settings.HudFollowsHead.Value || menuOpen || fullScreenLayer || !FindHud()) return;

            RectInt rect;
            if (!TryGetHudRect(out rect)) return;
            _pixelRect = rect;
            _extract = EnsureTexture();
        }

        /// <summary>
        /// Inside the render context, right after the UI was drawn: copies the HUD's rectangle into the HUD texture and
        /// clears it from the UI picture, so it isn't shown on the UI screen as well.
        /// </summary>
        public void Extract(CommandBuffer commands, RenderTexture ui)
        {
            if (!_extract || ui == null || _texture == null) return;

            Rect uv = UvRect;
            commands.Blit(ui, _texture, uv.size, uv.position);
            _hasCopy = true;

            if (!EnsureClearMaterial()) return;
            commands.SetRenderTarget(ui);
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
            commands.DrawMesh(_quad, Matrix4x4.TRS(new Vector3(uv.center.x, uv.center.y, 0f), Quaternion.identity, new Vector3(uv.width, uv.height, 1f)),
                _clearMaterial, 0, 0);
        }

        /// <summary>How far in front of the eyes: the setting, but always nearer than the UI screen so it is never behind it.</summary>
        public static float Distance()
        {
            return Mathf.Min(Mathf.Clamp(Settings.HudDistance.Value, 0.3f, 3f), 0.85f * PanelLayout.Distance);
        }

        /// <summary>Where the HUD sits relative to the eyes (Unity camera space, +Z forward), turned to face them.</summary>
        public static void Placement(out Vector3 position, out Quaternion rotation)
        {
            float yaw = Mathf.Clamp(Settings.HudLeftRight.Value, -60f, 60f);
            float pitch = Mathf.Clamp(Settings.HudUpDown.Value, -60f, 60f);
            rotation = Quaternion.Euler(-pitch, yaw, 0f);
            position = rotation * new Vector3(0f, 0f, Distance());
        }

        /// <summary>
        /// Size in the headset, in metres: as big as the HUD looks on the UI screen (at the UI screen size setting),
        /// times the HUD size setting.
        /// </summary>
        public Vector2 SizeMeters()
        {
            // The real HUD's size while it is copied (even before the first copy lands) or its last copy is previewed.
            bool real = _texture != null && (_extract || (_hasCopy && Previewing));
            float widthShare = real ? _pixelRect.width / (float)Mathf.Max(1, Screen.width) : PlaceholderWidthShare;
            float heightShare = real ? _pixelRect.height / (float)Mathf.Max(1, Screen.height) : PlaceholderHeightShare;
            float panelWidth = 2f * Distance() * Mathf.Tan(Mathf.Clamp(Settings.PanelWidthDegrees.Value, 20f, 140f) * 0.5f * Mathf.Deg2Rad);
            float panelHeight = panelWidth / PanelLayout.Aspect;
            float scale = Mathf.Clamp(Settings.HudSize.Value, 0.3f, 3f);
            return new Vector2(widthShare * panelWidth * scale, heightShare * panelHeight * scale);
        }

        public void Release()
        {
            if (_texture != null)
            {
                _texture.Release();
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }
            if (_placeholder != null) UnityEngine.Object.Destroy(_placeholder);
            if (_clearMaterial != null) UnityEngine.Object.Destroy(_clearMaterial);
            if (_quad != null) UnityEngine.Object.Destroy(_quad);
            _placeholder = null;
            _clearMaterial = null;
            _quad = null;
            _hasCopy = false;
        }

        private bool FindHud()
        {
            if (_playerHud != null && _playerHud.gameObject.activeInHierarchy) return true;
            if (Time.unscaledTime < _nextFind) return false;
            _nextFind = Time.unscaledTime + FindInterval;

            if (!_typeLookupDone)
            {
                _typeLookupDone = true;
                _playerHudType = AccessTools.TypeByName("UI.MainHUD.PlayerHUD");
                if (_playerHudType == null) Log.Warn("The game's status HUD was not found (the game may have been updated); it stays on the UI screen.");
            }
            if (_playerHudType == null) return false;

            _playerHud = UnityEngine.Object.FindObjectOfType(_playerHudType) as Component;
            return _playerHud != null && _playerHud.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// The HUD's rectangle in screen pixels (origin bottom left): the bounds of what its elements actually draw, so
        /// containers stretched over the screen don't count, padded a little and snapped outward to a coarse grid so its
        /// size rarely changes.
        /// </summary>
        private bool TryGetHudRect(out RectInt rect)
        {
            rect = default(RectInt);
            var document = _playerHud.GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null || root.panel == null) return false;
            Rect panelRect = root.panel.visualTree.worldBound;
            if (panelRect.width < 1f || panelRect.height < 1f) return false;

            if (!ReferenceEquals(root, _cachedRoot))
            {
                // The document rebuilds its tree when it is re-enabled; look the containers up again then.
                _cachedRoot = root;
                _hudElements.Clear();
                foreach (string name in HudElementNames)
                {
                    VisualElement element = root.Q(name);
                    if (element != null) _hudElements.Add(element);
                }
            }

            var bounds = new Rect();
            bool any = false;
            foreach (VisualElement element in _hudElements) AddContent(element, panelRect, ref bounds, ref any);
            if (!any) return false;

            float scaleX = Screen.width / panelRect.width;
            float scaleY = Screen.height / panelRect.height;
            int xMin = Snap((bounds.xMin - panelRect.xMin) * scaleX - Padding, false);
            int xMax = Snap((bounds.xMax - panelRect.xMin) * scaleX + Padding, true);
            int yMin = Snap(Screen.height - (bounds.yMax - panelRect.yMin) * scaleY - Padding, false);
            int yMax = Snap(Screen.height - (bounds.yMin - panelRect.yMin) * scaleY + Padding, true);
            xMin = Mathf.Clamp(xMin, 0, Screen.width);
            xMax = Mathf.Clamp(xMax, 0, Screen.width);
            yMin = Mathf.Clamp(yMin, 0, Screen.height);
            yMax = Mathf.Clamp(yMax, 0, Screen.height);
            if (xMax - xMin < 8 || yMax - yMin < 8) return false;

            rect = new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
            return true;
        }

        private static int Snap(float value, bool up)
        {
            return (up ? Mathf.CeilToInt(value / Grid) : Mathf.FloorToInt(value / Grid)) * Grid;
        }

        private static void AddContent(VisualElement element, Rect panelRect, ref Rect bounds, ref bool any)
        {
            IResolvedStyle style = element.resolvedStyle;
            if (style.display == DisplayStyle.None || style.visibility != Visibility.Visible || style.opacity < 0.01f) return;

            Rect box = element.worldBound;
            if (box.width > 0.5f && box.height > 0.5f && box.width < panelRect.width * MaxShareOfScreen &&
                box.height < panelRect.height * MaxShareOfScreen && Draws(element, style))
            {
                bounds = any ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, box.xMin), Mathf.Min(bounds.yMin, box.yMin),
                    Mathf.Max(bounds.xMax, box.xMax), Mathf.Max(bounds.yMax, box.yMax)) : box;
                any = true;
            }

            for (int i = 0; i < element.hierarchy.childCount; i++) AddContent(element.hierarchy[i], panelRect, ref bounds, ref any);
        }

        /// <summary>Whether the element paints anything itself: a background colour or image, a border or text.</summary>
        private static bool Draws(VisualElement element, IResolvedStyle style)
        {
            if (style.backgroundColor.a > 0.02f) return true;
            Background image = style.backgroundImage;
            if (image.texture != null || image.sprite != null || image.vectorImage != null || image.renderTexture != null) return true;
            if ((style.borderLeftWidth > 0f && style.borderLeftColor.a > 0.02f) || (style.borderTopWidth > 0f && style.borderTopColor.a > 0.02f) ||
                (style.borderRightWidth > 0f && style.borderRightColor.a > 0.02f) || (style.borderBottomWidth > 0f && style.borderBottomColor.a > 0.02f)) return true;
            var text = element as TextElement;
            return text != null && !string.IsNullOrEmpty(text.text);
        }

        private bool EnsureTexture()
        {
            if (_texture != null && _texture.width == _pixelRect.width && _texture.height == _pixelRect.height && _texture.IsCreated()) return true;
            if (_texture != null)
            {
                _texture.Release();
                UnityEngine.Object.Destroy(_texture);
            }
            _texture = new RenderTexture(_pixelRect.width, _pixelRect.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "VoidCrewVRUIFix_HUD",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _hasCopy = false;
            if (!_texture.Create())
            {
                Log.WarnOnce("hud-texture", "Could not create the HUD texture; the status HUD stays on the UI screen.");
                return false;
            }
            Log.Verbose("Status HUD texture is now " + _pixelRect.width + "x" + _pixelRect.height + ".");
            return true;
        }

        /// <summary>A material that overwrites pixels with fully transparent black (Unity's built-in colour shader).</summary>
        private bool EnsureClearMaterial()
        {
            if (_clearMaterial != null && _quad != null) return true;
            if (_clearLookupDone) return false;
            _clearLookupDone = true;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Log.Warn("Unity's built-in colour shader was not found, so the status HUD also stays on the UI screen.");
                return false;
            }
            _clearMaterial = new Material(shader) { name = "VoidCrewVRUIFix HUD cut-out", hideFlags = HideFlags.HideAndDontSave };
            _clearMaterial.SetColor("_Color", new Color(0f, 0f, 0f, 0f));
            _clearMaterial.SetInt("_SrcBlend", (int)BlendMode.One);
            _clearMaterial.SetInt("_DstBlend", (int)BlendMode.Zero);
            _clearMaterial.SetInt("_ZWrite", 0);
            _clearMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            _clearMaterial.SetInt("_Cull", (int)CullMode.Off);
            _quad = RenderUtil.CreateQuad(false);
            return true;
        }

        /// <summary>A dark box with "HUD" in it, shown while adjusting the HUD before it has been on screen.</summary>
        private Texture Placeholder()
        {
            if (_placeholder != null) return _placeholder;

            const int width = 270;
            const int height = 100;
            const int scale = 6;
            var pixels = new Color32[width * height];
            var fill = new Color32(10, 18, 26, 190);
            var edge = new Color32(230, 230, 230, 255);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool border = x < 3 || y < 3 || x >= width - 3 || y >= height - 3;
                    pixels[y * width + x] = border ? edge : fill;
                }
            }
            const string label = "HUD";
            PixelFont.Draw(pixels, width, height, label, (width - PixelFont.Width(label, scale)) / 2, (height - PixelFont.GlyphHeight * scale) / 2, scale, edge);

            _placeholder = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "VoidCrewVRUIFix HUD placeholder",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _placeholder.SetPixels32(pixels);
            _placeholder.Apply(false, true);
            return _placeholder;
        }
    }
}
