using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace VoidCrewVRUIFix
{
    /// <summary>A picture shown locked to the head (a HUD piece, the FPS counter).</summary>
    internal struct HeadLockedPicture
    {
        /// <summary>Null when it isn't shown.</summary>
        public Texture Texture;

        /// <summary>Its place relative to the eyes (Unity camera space, +Z forward), facing them.</summary>
        public Vector3 Position;
        public Quaternion Rotation;

        /// <summary>Its size in metres.</summary>
        public Vector2 Size;

        /// <summary>True when it is a piece of the UI picture and so follows the Flip UI setting.</summary>
        public bool Flip;

        /// <summary>1 is solid; see-through while the Modify UI window is open and another piece is chosen.</summary>
        public float Alpha;
    }

    /// <summary>
    /// A piece of the game's HUD shown as its own layer locked to your head, so it stays in view wherever you look, at a
    /// place you choose in the Modify UI window. In each gameplay frame the piece's rectangle is copied out of the
    /// captured UI into its own texture and cleared from the UI screen. The crosshair, markers and prompts stay on the UI
    /// screen, lined up with where you aim. While one of its settings is being changed, or the Modify UI window is open,
    /// the last copy (or a labelled box) is shown even with menus open, so you can see where it goes.
    /// </summary>
    internal abstract class HudLayer
    {
        private const float PreviewSeconds = 6f;
        protected const float MaxShareOfScreen = 0.6f;

        public readonly string Id;
        public readonly LayerPlacement Placement;
        private readonly string _placeholderLabel;
        private readonly Vector2 _placeholderShare;
        private readonly int _grid;
        private readonly int _padding;

        private RectInt _pixelRect;
        private bool _extract;
        private RenderTexture _texture;
        private bool _hasCopy;
        private Texture2D _placeholder;
        private float _previewUntil = -1f;
        private Material _clearMaterial;
        private bool _clearLookupDone;
        private Mesh _quad;

        /// <param name="id">Short name for its SteamVR overlay and the log, e.g. "hud".</param>
        /// <param name="placeholderLabel">Shown in the placeholder box while adjusting before the piece has been on screen.</param>
        /// <param name="placeholderShare">The piece's usual size as a share of the screen, used for the placeholder.</param>
        /// <param name="grid">The copied rectangle snaps outward to this many pixels.</param>
        /// <param name="padding">Pixels added around what the piece draws.</param>
        protected HudLayer(string id, LayerPlacement placement, string placeholderLabel, Vector2 placeholderShare, int grid, int padding)
        {
            Id = id;
            Placement = placement;
            _placeholderLabel = placeholderLabel;
            _placeholderShare = placeholderShare;
            _grid = grid;
            _padding = padding;
        }

        /// <summary>True when the piece is copied out of this frame's UI picture.</summary>
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
                if (!Placement.Enabled) return null;
                if (_extract) return _texture;
                if (!Previewing && !LayerPlacement.Editing) return null;
                return _hasCopy && _texture != null ? (Texture)_texture : Placeholder();
            }
        }

        /// <summary>What to show locked to the head this frame, and where.</summary>
        public HeadLockedPicture Picture()
        {
            var picture = new HeadLockedPicture { Texture = DisplayTexture, Flip = Settings.FlipVertically.Value, Alpha = Placement.Alpha };
            Placement.Place(out picture.Position, out picture.Rotation);
            picture.Size = SizeMeters();
            return picture;
        }

        /// <summary>Changing any of this piece's settings shows it for a few seconds, even with the Settings menu open.</summary>
        public void PreviewOnChange()
        {
            Placement.OnChanged(StartPreview);
        }

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
            if (!Placement.Enabled) return "stays on the UI screen";
            return DescribeSources() + (_extract ? ", copied this frame from " + _pixelRect.width + "x" + _pixelRect.height + " at " + _pixelRect.x + "," + _pixelRect.y : "") +
                   (Previewing ? ", previewing" : "") + " | " + Placement.Describe();
        }

        /// <summary>
        /// Main thread, once per frame while VR is on: finds the piece and decides whether to copy it this frame. Not
        /// while a menu or terminal is open, or a full-screen layer covers the screen, since its rectangle then shows
        /// something else.
        /// </summary>
        public void Update(bool menuOpen, bool fullScreenLayer)
        {
            bool wasExtracting = _extract;
            _extract = false;
            if (!Placement.Enabled || menuOpen || fullScreenLayer) return;

            Rect bounds;
            RectInt rect;
            if (!TryGetContentBounds(out bounds) || !ToPixelRect(bounds, out rect)) return;

            // While the piece is copied frame after frame its rectangle only grows, so its texture isn't made again
            // every time one of its elements appears, changes size or moves.
            if (wasExtracting && _texture != null)
            {
                int xMin = Mathf.Min(rect.xMin, _pixelRect.xMin);
                int yMin = Mathf.Min(rect.yMin, _pixelRect.yMin);
                rect = new RectInt(xMin, yMin, Mathf.Max(rect.xMax, _pixelRect.xMax) - xMin, Mathf.Max(rect.yMax, _pixelRect.yMax) - yMin);
            }
            _pixelRect = rect;
            _extract = EnsureTexture();
        }

        /// <summary>The bounds of what the piece draws this frame, in screen pixels (origin bottom left).</summary>
        protected abstract bool TryGetContentBounds(out Rect bounds);

        /// <summary>For the diagnostics report: whether the game's pieces were found.</summary>
        protected abstract string DescribeSources();

        /// <summary>
        /// Inside the render context, right after the UI was drawn: copies the piece's rectangle into its texture. Every
        /// piece is copied before any is cut out (Cut), so pieces whose rectangles overlap don't lose each other's pixels.
        /// </summary>
        public void Copy(CommandBuffer commands, RenderTexture ui)
        {
            if (!_extract || ui == null || _texture == null) return;
            Rect uv = UvRect;
            commands.Blit(ui, _texture, uv.size, uv.position);
            _hasCopy = true;
        }

        /// <summary>Clears the piece's rectangle from the UI picture, so it isn't shown on the UI screen as well.</summary>
        public void Cut(CommandBuffer commands, RenderTexture ui)
        {
            if (!_extract || ui == null || _texture == null || !EnsureClearMaterial()) return;
            Rect uv = UvRect;
            commands.SetRenderTarget(ui);
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
            commands.DrawMesh(_quad, Matrix4x4.TRS(new Vector3(uv.center.x, uv.center.y, 0f), Quaternion.identity, new Vector3(uv.width, uv.height, 1f)),
                _clearMaterial, 0, 0);
        }

        /// <summary>
        /// Size in the headset, in metres: as big as the piece looks on the UI screen (at the UI screen size setting),
        /// times the size setting.
        /// </summary>
        private Vector2 SizeMeters()
        {
            Vector2 share = Share();
            float panelWidth = 2f * Placement.EffectiveDistance() * Mathf.Tan(Mathf.Clamp(Settings.PanelWidthDegrees.Value, 20f, 140f) * 0.5f * Mathf.Deg2Rad);
            float panelHeight = panelWidth / PanelLayout.Aspect;
            return new Vector2(share.x * panelWidth * Placement.Scale, share.y * panelHeight * Placement.Scale);
        }

        /// <summary>How much of your view the piece covers, in degrees across and up (for the Modify UI window's picture).</summary>
        public Vector2 AngularSize()
        {
            Vector2 share = Share();
            float tanHalfWidth = Mathf.Tan(Mathf.Clamp(Settings.PanelWidthDegrees.Value, 20f, 140f) * 0.5f * Mathf.Deg2Rad);
            float tanHalfHeight = tanHalfWidth / PanelLayout.Aspect;
            return new Vector2(2f * Mathf.Atan(share.x * Placement.Scale * tanHalfWidth) * Mathf.Rad2Deg,
                2f * Mathf.Atan(share.y * Placement.Scale * tanHalfHeight) * Mathf.Rad2Deg);
        }

        /// <summary>The piece's share of the screen: its real size while it is copied or its last copy shows, else the placeholder's.</summary>
        private Vector2 Share()
        {
            bool real = _texture != null && (_extract || _hasCopy);
            return real
                ? new Vector2(_pixelRect.width / (float)Mathf.Max(1, Screen.width), _pixelRect.height / (float)Mathf.Max(1, Screen.height))
                : _placeholderShare;
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

        /// <summary>The content bounds, padded a little and snapped outward to the grid, in whole pixels.</summary>
        private bool ToPixelRect(Rect bounds, out RectInt rect)
        {
            rect = default(RectInt);
            int xMin = Mathf.Clamp(Snap(bounds.xMin - _padding, false), 0, Screen.width);
            int xMax = Mathf.Clamp(Snap(bounds.xMax + _padding, true), 0, Screen.width);
            int yMin = Mathf.Clamp(Snap(bounds.yMin - _padding, false), 0, Screen.height);
            int yMax = Mathf.Clamp(Snap(bounds.yMax + _padding, true), 0, Screen.height);
            if (xMax - xMin < 8 || yMax - yMin < 8) return false;
            rect = new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
            return true;
        }

        private int Snap(float value, bool up)
        {
            return (up ? Mathf.CeilToInt(value / _grid) : Mathf.FloorToInt(value / _grid)) * _grid;
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
                name = "VoidCrewVRUIFix " + Id,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _hasCopy = false;
            if (!_texture.Create())
            {
                Log.WarnOnce("hud-texture-" + Id, "Could not create the " + Placement.Title.ToLowerInvariant() + " texture; it stays on the UI screen.");
                return false;
            }
            Log.Verbose("The " + Placement.Title.ToLowerInvariant() + " texture is now " + _pixelRect.width + "x" + _pixelRect.height + ".");
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
                Log.Warn("Unity's built-in colour shader was not found, so the " + Placement.Title.ToLowerInvariant() + " also stays on the UI screen.");
                return false;
            }
            _clearMaterial = new Material(shader) { name = "VoidCrewVRUIFix " + Id + " cut-out", hideFlags = HideFlags.HideAndDontSave };
            _clearMaterial.SetColor("_Color", new Color(0f, 0f, 0f, 0f));
            _clearMaterial.SetInt("_SrcBlend", (int)BlendMode.One);
            _clearMaterial.SetInt("_DstBlend", (int)BlendMode.Zero);
            _clearMaterial.SetInt("_ZWrite", 0);
            _clearMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            _clearMaterial.SetInt("_Cull", (int)CullMode.Off);
            _quad = RenderUtil.CreateQuad(false);
            return true;
        }

        /// <summary>A dark box with the piece's label in it, shown while adjusting before the piece has been on screen.</summary>
        private Texture Placeholder()
        {
            if (_placeholder != null) return _placeholder;

            const int scale = 6;
            int height = 100;
            int width = Mathf.Max(PixelFont.Width(_placeholderLabel, scale) + 40, Mathf.RoundToInt(height * _placeholderShare.x / _placeholderShare.y * PanelLayout.Aspect));
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
            PixelFont.Draw(pixels, width, height, _placeholderLabel, (width - PixelFont.Width(_placeholderLabel, scale)) / 2,
                (height - PixelFont.GlyphHeight * scale) / 2, scale, edge);

            _placeholder = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "VoidCrewVRUIFix " + Id + " placeholder",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _placeholder.SetPixels32(pixels);
            _placeholder.Apply(false, true);
            return _placeholder;
        }
    }

    /// <summary>A game HUD component and the containers of its UI document that make up one piece.</summary>
    internal sealed class UiToolkitSource
    {
        public readonly string TypeName;
        public readonly string[] ElementNames;
        public readonly List<VisualElement> Elements = new List<VisualElement>();
        public Type Type;
        public bool TypeLookupDone;
        public FieldInfo DocumentField;
        public Component Component;
        public VisualElement CachedRoot;

        public UiToolkitSource(string typeName, params string[] elementNames)
        {
            TypeName = typeName;
            ElementNames = elementNames;
        }

        /// <summary>The component's UI document: its "Doc" field if it has one, else the document on its own object.</summary>
        public UIDocument Document()
        {
            var document = DocumentField != null ? DocumentField.GetValue(Component) as UIDocument : null;
            return document != null ? document : Component.GetComponent<UIDocument>();
        }
    }

    /// <summary>A HUD piece made of UI Toolkit elements (most of Void Crew's HUD).</summary>
    internal sealed class UiToolkitHudLayer : HudLayer
    {
        private const float FindInterval = 1f;

        private readonly UiToolkitSource[] _sources;
        private float _nextFind;

        public UiToolkitHudLayer(string id, LayerPlacement placement, string placeholderLabel, Vector2 placeholderShare, params UiToolkitSource[] sources)
            : base(id, placement, placeholderLabel, placeholderShare, 16, 6)
        {
            _sources = sources;
        }

        protected override string DescribeSources()
        {
            var parts = new List<string>();
            foreach (UiToolkitSource source in _sources)
            {
                parts.Add(source.TypeName + " " + (source.Component != null ? "found" : (source.TypeLookupDone && source.Type == null ? "missing from the game" : "not on screen")));
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>The bounds of what the piece's elements actually draw, so containers stretched over the screen don't count.</summary>
        protected override bool TryGetContentBounds(out Rect bounds)
        {
            bounds = default(Rect);
            if (!FindSources()) return false;

            // Screen pixels, origin top left like UI Toolkit.
            var screenBounds = new Rect();
            bool any = false;
            foreach (UiToolkitSource source in _sources)
            {
                if (source.Component == null || !source.Component.gameObject.activeInHierarchy) continue;
                UIDocument document = source.Document();
                VisualElement root = document != null ? document.rootVisualElement : null;
                if (root == null || root.panel == null) continue;
                Rect panelRect = root.panel.visualTree.worldBound;
                if (panelRect.width < 1f || panelRect.height < 1f) continue;

                if (!ReferenceEquals(root, source.CachedRoot))
                {
                    // The document rebuilds its tree when it is re-enabled; look the containers up again then.
                    source.CachedRoot = root;
                    source.Elements.Clear();
                    foreach (string name in source.ElementNames)
                    {
                        VisualElement element = root.Q(name);
                        if (element != null) source.Elements.Add(element);
                    }
                }

                var content = new Rect();
                bool found = false;
                foreach (VisualElement element in source.Elements)
                {
                    if (Visible(element)) AddContent(element, panelRect, ref content, ref found);
                }
                if (!found) continue;

                // Each document can scale its panel differently; convert to screen pixels per document.
                float scaleX = Screen.width / panelRect.width;
                float scaleY = Screen.height / panelRect.height;
                Rect onScreen = Rect.MinMaxRect((content.xMin - panelRect.xMin) * scaleX, (content.yMin - panelRect.yMin) * scaleY,
                    (content.xMax - panelRect.xMin) * scaleX, (content.yMax - panelRect.yMin) * scaleY);
                screenBounds = any ? Rect.MinMaxRect(Mathf.Min(screenBounds.xMin, onScreen.xMin), Mathf.Min(screenBounds.yMin, onScreen.yMin),
                    Mathf.Max(screenBounds.xMax, onScreen.xMax), Mathf.Max(screenBounds.yMax, onScreen.yMax)) : onScreen;
                any = true;
            }
            if (!any) return false;

            bounds = Rect.MinMaxRect(screenBounds.xMin, Screen.height - screenBounds.yMax, screenBounds.xMax, Screen.height - screenBounds.yMin);
            return true;
        }

        /// <summary>False when the element or one of its parents is hidden (the game hides whole documents this way).</summary>
        private static bool Visible(VisualElement element)
        {
            for (VisualElement e = element; e != null; e = e.hierarchy.parent)
            {
                IResolvedStyle style = e.resolvedStyle;
                if (style.display == DisplayStyle.None || style.visibility != Visibility.Visible || style.opacity < 0.01f) return false;
            }
            return true;
        }

        /// <summary>True when at least one of the piece's components is on screen. Missing ones are looked for once a second.</summary>
        private bool FindSources()
        {
            bool searchNow = Time.unscaledTime >= _nextFind;
            if (searchNow) _nextFind = Time.unscaledTime + FindInterval;

            bool any = false;
            foreach (UiToolkitSource source in _sources)
            {
                if (source.Component == null || !source.Component.gameObject.activeInHierarchy)
                {
                    if (!searchNow) continue;
                    if (!source.TypeLookupDone)
                    {
                        source.TypeLookupDone = true;
                        source.Type = AccessTools.TypeByName(source.TypeName);
                        if (source.Type == null)
                        {
                            Log.Warn("The game's " + source.TypeName + " HUD was not found (the game may have been updated); it stays on the UI screen.");
                        }
                        else
                        {
                            // Some HUDs keep their document in a Doc field, others only as a component (no HarmonyX warning for those).
                            source.DocumentField = FindField(source.Type, "Doc");
                            if (source.DocumentField != null && source.DocumentField.FieldType != typeof(UIDocument)) source.DocumentField = null;
                        }
                    }
                    if (source.Type == null) continue;
                    source.Component = UnityEngine.Object.FindObjectOfType(source.Type) as Component;
                    if (source.Component == null || !source.Component.gameObject.activeInHierarchy) continue;
                }
                any = true;
            }
            return any;
        }

        /// <summary>A field of the type or one of its base types, or null.</summary>
        private static FieldInfo FindField(Type type, string name)
        {
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (; type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, Any);
                if (field != null) return field;
            }
            return null;
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
    }

    /// <summary>
    /// A piece of the game's turret screen (uGUI), while you control a turret. Its rectangle is either what its parts
    /// draw, or a fixed box around the middle of the screen (the crosshair, whose aiming frame sways as the turret turns).
    /// </summary>
    internal sealed class UguiHudLayer : HudLayer
    {
        private const float FindInterval = 1f;
        private const string ScreenTypeName = "CG.Client.Player.HUD.HUD_WeaponControlScreen";

        private readonly string[] _pieces;
        private readonly Vector2 _fixedCanvasSize;
        private readonly List<Graphic> _graphics = new List<Graphic>();
        private readonly List<Graphic> _found = new List<Graphic>();
        private readonly Vector3[] _corners = new Vector3[4];
        private Type _screenType;
        private bool _typeLookupDone;
        private Component _screen;
        private Canvas _canvas;
        private float _nextFind;
        private int _piecesFound;
        private int _drawing;

        /// <param name="pieces">Paths under the turret screen, from the game's own turret screen layout.</param>
        /// <param name="fixedCanvasSize">Zero: the rectangle is what the pieces draw. Otherwise a box this size (canvas units) around the screen's middle.</param>
        public UguiHudLayer(string id, LayerPlacement placement, string placeholderLabel, Vector2 placeholderShare, string[] pieces, Vector2 fixedCanvasSize)
            : base(id, placement, placeholderLabel, placeholderShare, 8, 4)
        {
            _pieces = pieces;
            _fixedCanvasSize = fixedCanvasSize;
        }

        protected override string DescribeSources()
        {
            if (_typeLookupDone && _screenType == null) return "the game's turret screen is missing from the game";
            if (_screen == null) return "turret screen not found yet";
            return "turret screen found, " + _piecesFound + " of " + _pieces.Length + " parts, " + _drawing + " drawing";
        }

        protected override bool TryGetContentBounds(out Rect bounds)
        {
            bounds = default(Rect);
            _drawing = 0;
            if (!PlayerModes.InTurret() || !FindScreen()) return false;

            // Only overlay canvases are part of the captured UI, and their corners are in screen pixels.
            Canvas root = _canvas != null ? _canvas.rootCanvas : null;
            if (root == null || root.renderMode != RenderMode.ScreenSpaceOverlay) return false;

            float maxWidth = Screen.width * MaxShareOfScreen;
            float maxHeight = Screen.height * MaxShareOfScreen;
            bool any = false;
            foreach (Graphic graphic in _graphics)
            {
                if (graphic == null || !graphic.isActiveAndEnabled) continue;
                CanvasRenderer renderer = graphic.canvasRenderer;
                if (renderer == null || renderer.cull || renderer.GetInheritedAlpha() * graphic.color.a < 0.02f) continue;

                graphic.rectTransform.GetWorldCorners(_corners);
                float xMin = Mathf.Min(Mathf.Min(_corners[0].x, _corners[1].x), Mathf.Min(_corners[2].x, _corners[3].x));
                float xMax = Mathf.Max(Mathf.Max(_corners[0].x, _corners[1].x), Mathf.Max(_corners[2].x, _corners[3].x));
                float yMin = Mathf.Min(Mathf.Min(_corners[0].y, _corners[1].y), Mathf.Min(_corners[2].y, _corners[3].y));
                float yMax = Mathf.Max(Mathf.Max(_corners[0].y, _corners[1].y), Mathf.Max(_corners[2].y, _corners[3].y));
                if (xMax - xMin < 0.5f || yMax - yMin < 0.5f || xMax - xMin > maxWidth || yMax - yMin > maxHeight) continue;

                bounds = any ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, xMin), Mathf.Min(bounds.yMin, yMin), Mathf.Max(bounds.xMax, xMax), Mathf.Max(bounds.yMax, yMax))
                             : Rect.MinMaxRect(xMin, yMin, xMax, yMax);
                any = true;
                _drawing++;
            }
            if (!any || _fixedCanvasSize == Vector2.zero) return any;

            Vector2 half = _fixedCanvasSize * 0.5f * Mathf.Max(0.01f, root.scaleFactor);
            var middle = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            bounds = Rect.MinMaxRect(middle.x - half.x, middle.y - half.y, middle.x + half.x, middle.y + half.y);
            return true;
        }

        /// <summary>
        /// True when the turret screen and the piece's parts are known. They are looked up again once a second, which also
        /// picks up ammo socket entries the game adds while you use the turret.
        /// </summary>
        private bool FindScreen()
        {
            if (Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + FindInterval;
                if (_screen == null)
                {
                    if (!_typeLookupDone)
                    {
                        _typeLookupDone = true;
                        _screenType = AccessTools.TypeByName(ScreenTypeName);
                        if (_screenType == null) Log.WarnOnce("turret-screen", "The game's turret screen was not found (the game may have been updated); its pieces stay on the UI screen.");
                    }
                    if (_screenType != null) _screen = UnityEngine.Object.FindObjectOfType(_screenType) as Component;
                }
                CollectGraphics();
            }
            return _screen != null && _graphics.Count > 0;
        }

        private void CollectGraphics()
        {
            _graphics.Clear();
            _piecesFound = 0;
            _canvas = _screen != null ? _screen.GetComponentInParent<Canvas>() : null;
            if (_screen == null) return;

            Transform root = _screen.transform;
            foreach (string path in _pieces)
            {
                Transform piece = root.Find(path);
                if (piece == null) continue;
                _piecesFound++;
                piece.GetComponentsInChildren(true, _found);
                _graphics.AddRange(_found);
            }
            if (_piecesFound == 0)
            {
                Log.WarnOnce("turret-pieces-" + Id, "The turret screen's " + Placement.Title.ToLowerInvariant() + " was not found (the game may have been updated); it stays on the UI screen.");
            }
        }
    }

    /// <summary>Every HUD piece that can be locked to the head, in the order they are copied and listed.</summary>
    internal static class HudLayers
    {
        public static HudLayer PlayerHud { get; private set; }
        public static HudLayer ShipStatus { get; private set; }
        public static HudLayer Objectives { get; private set; }
        public static HudLayer TurretCrosshair { get; private set; }
        public static HudLayer TurretReadouts { get; private set; }
        public static HudLayer WeaponStats { get; private set; }
        public static HudLayer Crew { get; private set; }
        public static HudLayer HubMission { get; private set; }
        public static HudLayer GamePrivacy { get; private set; }
        public static HudLayer[] All { get; private set; } = new HudLayer[0];

        /// <summary>The SteamVR overlay key of the FPS counter, which is shown with the pieces.</summary>
        public const string FpsOverlayKey = "voidcrew.vruifix.fps";

        public static void Create()
        {
            if (All.Length > 0) return;

            PlayerHud = new UiToolkitHudLayer("hud", Settings.PlayerHud, "HUD", new Vector2(0.27f, 0.2f),
                new UiToolkitSource("UI.MainHUD.PlayerHUD", "LocalPlayerContainer", "KeybindsContainer"),
                new UiToolkitSource("ClassAbilityDisplay", "ClassAbilityContainer"));
            ShipStatus = new UiToolkitHudLayer("shipstatus", Settings.ShipStatus, "SHIP", new Vector2(0.17f, 0.09f),
                new UiToolkitSource("UI.MainHUD.ShipInfoHUD", "ShipInfo"));
            Objectives = new UiToolkitHudLayer("objectives", Settings.Objectives, "OBJECTIVES", new Vector2(0.18f, 0.22f),
                new UiToolkitSource("MissionListUIController", "MissionListContainer"));
            TurretCrosshair = new UguiHudLayer("crosshair", Settings.TurretCrosshair, "CROSSHAIR", new Vector2(0.4f, 0.4f),
                new[]
                {
                    "_HitReticle", "_FeederRenderer", "_WeaponControlFrameAnimation", "_WeaponControlStatRenderer", "_IndependentTurretHealth",
                    "_MagazineRenderer/HeatSinkMagazineGizmo/_EmergencyCooling_CycleThroughChildren", "Fluff and flavor"
                },
                // The aiming frame (720 x 420) and a little room for it to sway; the ammo and range readouts start just below.
                new Vector2(780f, 440f));
            TurretReadouts = new UguiHudLayer("turrethud", Settings.TurretReadouts, "TURRET", new Vector2(0.48f, 0.11f),
                new[]
                {
                    "_MagazineRenderer/HeatSinkMagazineGizmo/TemperatureBar", "_MagazineRenderer/HeatSinkMagazineGizmo/_HeatSinkWarningBarGroup",
                    "_MagazineRenderer/PowerDrainMagazineGizmo/ChargeBar", "_MagazineRenderer/BulletMagazineGizmo_AmmoInfo",
                    "_MagazineRenderer/BatteryMagazineGizmo", "_MagazineRenderer/WeaponDataGizmo", "_WeaponControlRelativeDirection"
                },
                Vector2.zero);
            WeaponStats = new UiToolkitHudLayer("weaponstats", Settings.WeaponStats, "STATS", new Vector2(0.15f, 0.3f),
                new UiToolkitSource("UI.PilotHUD.WeaponHUD", "ModifierContainer"));
            // The crew list: one entry per crewmate (name, level, class, health), added as they join.
            Crew = new UiToolkitHudLayer("crew", Settings.Crew, "CREW", new Vector2(0.12f, 0.12f),
                new UiToolkitSource("UI.MainHUD.CoopBuddiesPanel", "CoopBuddiesContainer"));
            // Hub only. The game slides the mission panel off the screen when it isn't needed; then nothing is copied.
            HubMission = new UiToolkitHudLayer("hubmission", Settings.HubMission, "MISSION", new Vector2(0.18f, 0.07f),
                new UiToolkitSource("HUBFlowDisplay", "HubFlowPanel"));
            GamePrivacy = new UiToolkitHudLayer("privacy", Settings.GamePrivacy, "PUBLIC", new Vector2(0.07f, 0.025f),
                new UiToolkitSource("HUBFlowDisplay", "PrivacyDisplay"));
            All = new[] { PlayerHud, ShipStatus, Objectives, TurretCrosshair, TurretReadouts, WeaponStats, Crew, HubMission, GamePrivacy };
        }

        /// <summary>The SteamVR overlay key for a piece; the player HUD and turret HUD keep the keys earlier versions used.</summary>
        public static string OverlayKey(HudLayer layer)
        {
            return "voidcrew.vruifix." + layer.Id;
        }
    }
}
