using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Makes HDRP's DLSS work on the VR camera. In multi-pass VR, DLSS keeps one state per eye and picks it with
    /// XRPass.multipassId. That id numbers every camera pass drawn so far in the frame, not the camera's own eyes, so
    /// when another camera is drawn first (the game's interaction-glow camera, the intro video camera) the VR camera's
    /// eyes become passes 2 and 3 and DLSS throws "Index was out of range" on every frame. While DLSS records its
    /// commands, the id is swapped for the eye's number within its own camera, then put back.
    /// </summary>
    internal static class DlssEyes
    {
        private static Func<HDCamera, XRPass> _xrOf;
        private static Action<XRPass, int> _setPassId;
        private static FieldInfo _layout;
        private static readonly HashSet<int> Reported = new HashSet<int>();
        private static Camera _lastCamera;
        private static int _lastPassId;
        private static int _lastEye;

        // What DLSS last did, so the diagnostics report can show whether it is really running.
        private static Camera _runCamera;
        private static int _runFrame = -1;
        private static int _eyesThisFrame;
        private static int _eyesLastFrame;
        private static Vector2Int _renderSize;
        private static Vector2Int _outputSize;

        public static bool Active { get; private set; }
        public static int RenumberedPasses { get; private set; }

        /// <summary>True if DLSS upscaled the VR camera within the last <paramref name="frames"/> frames.</summary>
        public static bool RanWithin(int frames)
        {
            return _runCamera != null && Time.frameCount - _runFrame <= frames;
        }

        /// <summary>For the diagnostics report.</summary>
        public static string Describe()
        {
            if (!Active) return "eye fix off";

            string state;
            if (_runCamera == null)
            {
                state = "not running (choose DLSS under Graphics > Upscaling)";
            }
            else if (Time.frameCount - _runFrame > 2)
            {
                state = "not running now (last ran " + (Time.frameCount - _runFrame) + " frames ago on '" + _runCamera.name + "')";
            }
            else
            {
                state = "running on '" + _runCamera.name + "' for " + Mathf.Max(_eyesThisFrame, _eyesLastFrame) + " eyes per frame, rendering " +
                        _renderSize.x + "x" + _renderSize.y + " per eye and upscaling to " + _outputSize.x + "x" + _outputSize.y;
            }
            return state + "; eye fix on, " + RenumberedPasses + " eye passes renumbered" +
                   (_lastCamera != null ? " (last: '" + _lastCamera.name + "' pass " + _lastPassId + " -> eye " + _lastEye + ")" : "");
        }

        /// <summary>Finds the HDRP internals the fix needs. Returns what is missing, or null when everything was found.</summary>
        public static string Resolve()
        {
            MethodInfo xrGetter = AccessTools.PropertyGetter(typeof(HDCamera), "xr");
            MethodInfo passIdSetter = AccessTools.PropertySetter(typeof(XRPass), "multipassId");
            _layout = AccessTools.Field(typeof(XRSystem), "s_Layout");
            if (xrGetter == null || passIdSetter == null || _layout == null)
            {
                return "HDCamera.xr, XRPass.multipassId or XRSystem.s_Layout not found";
            }

            _xrOf = (Func<HDCamera, XRPass>)Delegate.CreateDelegate(typeof(Func<HDCamera, XRPass>), xrGetter);
            _setPassId = (Action<XRPass, int>)Delegate.CreateDelegate(typeof(Action<XRPass, int>), passIdSetter);
            Active = true;
            return null;
        }

        /// <summary>Before DLSS runs: gives the pass its eye number. Returns the id to put back afterwards, or -1.</summary>
        public static int UseEyeNumber(HDCamera hdCamera)
        {
            RecordRun(hdCamera);
            XRPass pass = _xrOf(hdCamera);
            if (pass == null || !pass.enabled || pass.singlePassEnabled) return -1;

            int passId = pass.multipassId;
            int eye = EyeNumber(hdCamera.camera, pass);
            // HDRP keeps exactly two eye states in multi-pass VR.
            if (eye < 0 || eye > 1 || eye == passId) return -1;

            _setPassId(pass, eye);
            RenumberedPasses++;
            _lastCamera = hdCamera.camera;
            _lastPassId = passId;
            _lastEye = eye;
            if (Reported.Add(hdCamera.camera.GetInstanceID()))
            {
                Log.Info("DLSS in VR: other cameras are drawn before '" + hdCamera.camera.name + "', so HDRP numbered its eye " + eye +
                         " as pass " + passId + ". DLSS now looks each eye up by its eye number, which stops its 'Index was out of range' errors.");
            }
            return passId;
        }

        private static void RecordRun(HDCamera hdCamera)
        {
            int frame = Time.frameCount;
            if (frame != _runFrame)
            {
                _eyesLastFrame = _runFrame == frame - 1 ? _eyesThisFrame : 0;
                _eyesThisFrame = 0;
                _runFrame = frame;
            }
            _eyesThisFrame++;
            _runCamera = hdCamera.camera;
            _renderSize = new Vector2Int(hdCamera.actualWidth, hdCamera.actualHeight);
            DynamicResolutionHandler resolution = DynamicResolutionHandler.instance;
            if (resolution != null) _outputSize = resolution.finalViewport;
        }

        /// <summary>After DLSS ran (or failed): puts HDRP's own pass id back.</summary>
        public static void Restore(HDCamera hdCamera, int passId)
        {
            if (passId < 0) return;
            XRPass pass = _xrOf(hdCamera);
            if (pass != null) _setPassId(pass, passId);
        }

        /// <summary>Which of the camera's own passes this frame the given one is: 0 for the first eye, 1 for the second.</summary>
        private static int EyeNumber(Camera camera, XRPass pass)
        {
            var layout = _layout.GetValue(null) as XRLayout;
            if (layout == null) return -1;

            List<(Camera, XRPass)> passes = layout.GetActivePasses();
            int eye = 0;
            for (int i = 0; i < passes.Count; i++)
            {
                if (passes[i].Item1 != camera) continue;
                if (ReferenceEquals(passes[i].Item2, pass)) return eye;
                eye++;
            }
            return -1;
        }
    }
}
