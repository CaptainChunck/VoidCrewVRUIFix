using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// The start-up intro video is drawn on the camera's near plane, which shows nothing usable in VR. While VR is
    /// active it is redirected into a texture shown full-screen behind the rest of the UI, so it appears on the VR
    /// UI panel (and on the desktop window) like the rest of the screen-space UI.
    /// </summary>
    internal static class IntroVideo
    {
        private static VideoPlayer _player;
        private static RenderTexture _texture;
        private static Canvas _canvas;
        private static RawImage _image;

        public static string Describe()
        {
            if (_player == null) return "not active";
            return string.Format("player '{0}', playing {1}, shown {2}", _player.name, _player.isPlaying, _canvas != null && _canvas.enabled);
        }

        public static void Redirect(VideoPlayer player)
        {
            if (player == null) return;

            _player = player;
            if (_texture == null)
            {
                _texture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32)
                {
                    name = "VoidCrewVRUIFix_IntroVideo",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _texture.Create();
            }

            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = _texture;
            player.aspectRatio = VideoAspectRatio.FitInside;
            EnsureCanvas();
            _image.texture = _texture;
            Log.Info("Intro video moved onto the VR UI panel (the game draws it on the camera's near plane, which doesn't work in VR).");
        }

        /// <summary>Shows the video only while it plays, and frees the texture once the intro is gone.</summary>
        public static void Update()
        {
            bool show = _player != null && _player.isActiveAndEnabled && _player.isPlaying;
            if (_canvas != null && _canvas.enabled != show) _canvas.enabled = show;

            if (_player == null && _texture != null)
            {
                _texture.Release();
                Object.Destroy(_texture);
                _texture = null;
                if (_image != null) _image.texture = null;
            }
        }

        private static void EnsureCanvas()
        {
            if (_canvas != null) return;

            var root = new GameObject("VoidCrewVRUIFix_IntroVideo");
            Object.DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.HideAndDontSave;
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = -1000; // behind the intro's own text and fade canvases
            _canvas.enabled = false;

            var imageObject = new GameObject("Video", typeof(RectTransform));
            imageObject.transform.SetParent(root.transform, false);
            _image = imageObject.AddComponent<RawImage>();
            _image.raycastTarget = false;
            var rect = (RectTransform)imageObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
