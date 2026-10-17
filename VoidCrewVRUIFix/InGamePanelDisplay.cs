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

        private static Func<HDCamera, XRPass> _getXrPass;
        private static bool _xrLookupDone;

        private Material _material;
        private Mesh _quad;
        private Mesh _flippedQuad;

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            // uGUI's default material is always present in the build, needs no HDRP setup and can ignore depth.
            _material = new Material(Canvas.GetDefaultCanvasMaterial())
            {
                name = "VoidCrewVRUIFix Panel",
                hideFlags = HideFlags.HideAndDontSave
            };
            _material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            _material.SetVector("_TextureSampleAdd", Vector4.zero);
            _quad = CreateQuad(false);
            _flippedQuad = CreateQuad(true);
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

            Matrix4x4 anchor = GameCameraToWorld;
            if (HeadLocked)
            {
                // While the frame renders, UUVR has applied the head rotation to the camera.
                Transform cameraTransform = ctx.hdCamera.camera.transform;
                anchor = Matrix4x4.TRS(cameraTransform.position, cameraTransform.rotation, Vector3.one);
            }

            _material.mainTexture = Texture;
            ctx.cmd.DrawMesh(FlipVertically ? _flippedQuad : _quad, anchor * PanelInCamera, _material, 0, 0);
            LastDrawnFrame = Time.frameCount;
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_material);
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

        private static Mesh CreateQuad(bool flipped)
        {
            float bottom = flipped ? 1f : 0f;
            float top = flipped ? 0f : 1f;
            var mesh = new Mesh
            {
                name = flipped ? "VoidCrewVRUIFix Quad (flipped)" : "VoidCrewVRUIFix Quad",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
                },
                uv = new[]
                {
                    new Vector2(0f, bottom), new Vector2(1f, bottom),
                    new Vector2(1f, top), new Vector2(0f, top)
                },
                colors = new[] { Color.white, Color.white, Color.white, Color.white },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
