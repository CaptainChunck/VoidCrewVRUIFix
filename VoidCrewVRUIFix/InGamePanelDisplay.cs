using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Shows the UI as a panel drawn into the game image by an HDRP custom pass. It runs after post-processing, so
    /// bloom, depth of field and exposure don't touch it, and it is always drawn on top of the world.
    /// Works with both OpenVR and OpenXR; used when the SteamVR overlay isn't available.
    /// </summary>
    internal sealed class InGamePanelDisplay : IUiDisplay
    {
        private readonly GameObject _root;
        private readonly CustomPassVolume _volume;
        private readonly UiPanelPass _pass;

        public InGamePanelDisplay()
        {
            _root = new GameObject("VoidCrewVRUIFix_Panel");
            _root.SetActive(false); // configure the volume before it registers itself with HDRP
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.hideFlags = HideFlags.HideAndDontSave;

            _volume = _root.AddComponent<CustomPassVolume>();
            _volume.isGlobal = true;
            _volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            _volume.priority = 1000f;
            _pass = new UiPanelPass { name = "VoidCrewVRUIFix UI Panel" };
            _volume.customPasses.Add(_pass);

            _root.SetActive(true);
        }

        public string Name { get { return "in-game panel"; } }

        public bool IsAlive { get { return _root != null; } }

        public string Status
        {
            get
            {
                return string.Format("target camera '{0}', last drawn frame {1} (current {2})",
                    _volume.targetCamera != null ? _volume.targetCamera.name : "none",
                    UiPanelPass.LastDrawnFrame, Time.frameCount);
            }
        }

        public void SetTexture(RenderTexture texture)
        {
            _pass.Texture = texture;
        }

        public void Update(PanelState state)
        {
            if (_volume.targetCamera != state.Camera) _volume.targetCamera = state.Camera;

            _pass.Visible = state.Visible && state.Camera != null && _pass.Texture != null;
            _pass.FlipVertically = Settings.FlipVertically.Value;
            _pass.HeadLocked = Settings.Anchor.Value == PanelAnchor.Head;
            _pass.PanelInCamera = Matrix4x4.TRS(
                new Vector3(0f, PanelLayout.HeightOffset, PanelLayout.Distance),
                Quaternion.identity,
                new Vector3(PanelLayout.WidthMeters, PanelLayout.HeightMeters, 1f));
            _pass.GameCameraToWorld = Matrix4x4.TRS(state.CameraPosition, state.CameraRotation, Vector3.one);
            _pass.Fill = state.Fill;
            _pass.FpsTexture = state.FpsTexture;
            if (state.FpsTexture != null)
            {
                float distance = FpsCounter.Distance(PanelLayout.Distance);
                float width = FpsCounter.WidthAtOneMetre * distance;
                _pass.FpsInCamera = Matrix4x4.TRS(
                    new Vector3(FpsCounter.OffsetAtOneMetre.x * distance, FpsCounter.OffsetAtOneMetre.y * distance, distance),
                    Quaternion.identity, new Vector3(width, width * FpsCounter.Aspect, 1f));
            }
        }

        public void AppendSubmit(CommandBuffer commands)
        {
        }

        public void SubmitFromMainThread()
        {
        }

        public void Recenter()
        {
            // The panel follows the in-game view direction (or the head), so there is nothing to recentre.
        }

        public string CheckHealth()
        {
            if (_pass.Visible && UiPanelPass.LastDrawnFrame < Time.frameCount - 120)
            {
                return "The in-game UI panel is not being drawn. HDRP custom passes may be disabled for camera '" +
                       (_volume.targetCamera != null ? _volume.targetCamera.name : "none") + "'. Press F9 and include the diagnostics in a bug report.";
            }
            return null;
        }

        public void Shutdown()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }
    }

    /// <summary>Draws the UI texture on a quad in front of the camera, after post-processing, on top of everything.</summary>
    [Serializable]
    internal sealed class UiPanelPass : CustomPass
    {
        public static int LastDrawnFrame = -1;

        public Texture Texture;
        public bool Visible;
        public bool FlipVertically;
        public bool HeadLocked;
        public Matrix4x4 PanelInCamera = Matrix4x4.identity;
        public Matrix4x4 GameCameraToWorld = Matrix4x4.identity;
        public Color Fill;
        public Texture FpsTexture;
        public Matrix4x4 FpsInCamera = Matrix4x4.identity;

        private const float FillExtraDistance = 0.25f;
        private const float FillHalfAngle = 80f;

        private static Func<HDCamera, XRPass> _getXrPass;
        private static bool _xrLookupDone;

        private Material _material;
        private Material _fillMaterial;
        private Material _fpsMaterial;
        private Mesh _quad;
        private Mesh _flippedQuad;

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            _material = RenderUtil.CreateUiMaterial("VoidCrewVRUIFix Panel");
            _fillMaterial = RenderUtil.CreateUiMaterial("VoidCrewVRUIFix Panel Fill");
            _fillMaterial.mainTexture = Texture2D.whiteTexture;
            _fpsMaterial = RenderUtil.CreateUiMaterial("VoidCrewVRUIFix FPS counter");
            _quad = RenderUtil.CreateQuad(false);
            _flippedQuad = RenderUtil.CreateQuad(true);
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (!Visible || Texture == null || _material == null) return;

            Matrix4x4 view;
            Matrix4x4 projection;
            if (TryGetEyeMatrices(ctx.hdCamera, out view, out projection))
            {
                ctx.cmd.SetViewProjectionMatrices(view, projection);
            }

            // While the frame renders, UUVR has applied the head rotation to the camera.
            Transform cameraTransform = ctx.hdCamera.camera.transform;
            Matrix4x4 head = Matrix4x4.TRS(cameraTransform.position, cameraTransform.rotation, Vector3.one);
            Matrix4x4 anchor = HeadLocked ? head : GameCameraToWorld;

            if (Fill.a > 0.003f)
            {
                // Full-screen UI (death screen, fades): fill the whole view behind the panel, locked to the head.
                float distance = PanelInCamera.m23 + FillExtraDistance;
                float size = 2f * distance * Mathf.Tan(FillHalfAngle * Mathf.Deg2Rad);
                _fillMaterial.color = Fill;
                ctx.cmd.DrawMesh(_quad, head * Matrix4x4.TRS(new Vector3(0f, 0f, distance), Quaternion.identity, new Vector3(size, size, 1f)),
                    _fillMaterial, 0, 0);
            }

            _material.mainTexture = Texture;
            ctx.cmd.DrawMesh(FlipVertically ? _flippedQuad : _quad, anchor * PanelInCamera, _material, 0, 0);

            if (FpsTexture != null)
            {
                // The frame-rate counter, locked to the head like a SteamVR overlay would be.
                _fpsMaterial.mainTexture = FpsTexture;
                ctx.cmd.DrawMesh(_quad, head * FpsInCamera, _fpsMaterial, 0, 0);
            }
            LastDrawnFrame = Time.frameCount;
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_material);
            CoreUtils.Destroy(_fillMaterial);
            CoreUtils.Destroy(_fpsMaterial);
            CoreUtils.Destroy(_quad);
            CoreUtils.Destroy(_flippedQuad);
        }

        /// <summary>The view and projection of the eye being rendered. HDRP keeps these internal, hence reflection.</summary>
        private static bool TryGetEyeMatrices(HDCamera hdCamera, out Matrix4x4 view, out Matrix4x4 projection)
        {
            view = Matrix4x4.identity;
            projection = Matrix4x4.identity;

            if (!_xrLookupDone)
            {
                _xrLookupDone = true;
                PropertyInfo xrProperty = AccessTools.Property(typeof(HDCamera), "xr");
                MethodInfo getter = xrProperty != null ? xrProperty.GetGetMethod(true) : null;
                if (getter != null)
                {
                    _getXrPass = (Func<HDCamera, XRPass>)Delegate.CreateDelegate(typeof(Func<HDCamera, XRPass>), getter);
                }
                else
                {
                    Log.Warn("Could not find HDRP's per-eye camera data; the in-game panel will rely on HDRP's own camera matrices.");
                }
            }

            if (_getXrPass == null) return false;
            XRPass xr = _getXrPass(hdCamera);
            if (xr == null || !xr.enabled) return false;

            view = xr.GetViewMatrix(0);
            projection = xr.GetProjMatrix(0);
            return true;
        }
    }
}
