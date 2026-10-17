using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Video;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Harmony patches on the game and on Unity. Every target is looked up by name: if a game update renames one,
    /// that single fix is skipped with a warning and the rest of the plugin keeps working.
    /// </summary>
    internal static class GamePatches
    {
        private static FieldInfo _cursorTextureField;
        private static MethodInfo _updateCursorGraphic;

        /// <summary>The game's own cursor image, once its input system has been initialised.</summary>
        public static Texture2D GameCursorTexture
        {
            get { return _cursorTextureField != null ? _cursorTextureField.GetValue(null) as Texture2D : null; }
        }

        public static void Apply(Harmony harmony)
        {
            var applied = new List<string>();
            var skipped = new List<string>();

            Patch(harmony, "SettingUpscaling", "SupportsDLSS", nameof(HideDlss), null, "DLSS option", applied, skipped);
            Patch(harmony, "SettingUpscaling", "SupportsFSR3", nameof(HideFsr3), null, "FSR3 option", applied, skipped);
            Patch(harmony, "VideoSwitcher", "SetupAndPlayVideo", null, nameof(RedirectIntroVideo), "intro video", applied, skipped);
            Patch(harmony, "CG.Input.MouseCursor", "UpdateCursorGraphic", nameof(HideHardwareCursorGraphic), null, "Windows cursor", applied, skipped);

            // Patch both overloads: the one-argument version is small enough to be inlined into game code.
            PatchMethod(harmony, AccessTools.Method(typeof(Camera), "WorldToViewportPoint", new[] { typeof(Vector3) }),
                nameof(RemapWorldToViewportMono), null, "marker alignment", applied, skipped);
            PatchMethod(harmony, AccessTools.Method(typeof(Camera), "WorldToViewportPoint", new[] { typeof(Vector3), typeof(Camera.MonoOrStereoscopicEye) }),
                nameof(RemapWorldToViewport), null, "marker alignment (stereo overload)", applied, skipped);

            Type mouseCursor = AccessTools.TypeByName("CG.Input.MouseCursor");
            if (mouseCursor != null)
            {
                _cursorTextureField = AccessTools.Field(mouseCursor, "_mouseCursorTexture");
                _updateCursorGraphic = AccessTools.Method(mouseCursor, "UpdateCursorGraphic");
            }

            Log.Info("Patched: " + string.Join(", ", applied.ToArray()) + ".");
            if (skipped.Count > 0)
            {
                Log.Warn("Could not apply: " + string.Join("; ", skipped.ToArray()) +
                         ". The game may have been updated; those fixes are inactive, everything else still works.");
            }
        }

        /// <summary>Re-applies the game's cursor image choice, so the Windows cursor is hidden or restored to match VR.</summary>
        public static void RefreshHardwareCursor()
        {
            if (_updateCursorGraphic == null || GameCursorTexture == null) return;
            try
            {
                _updateCursorGraphic.Invoke(null, new object[] { true, true });
            }
            catch (Exception e)
            {
                Log.Failure("Updating the Windows cursor", e);
            }
        }

        private static void Patch(Harmony harmony, string typeName, string methodName, string prefix, string postfix,
            string label, List<string> applied, List<string> skipped)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo target = type != null ? AccessTools.DeclaredMethod(type, methodName) : null;
            if (target == null)
            {
                skipped.Add(label + " (" + typeName + "." + methodName + " not found)");
                return;
            }
            PatchMethod(harmony, target, prefix, postfix, label, applied, skipped);
        }

        private static void PatchMethod(Harmony harmony, MethodBase target, string prefix, string postfix,
            string label, List<string> applied, List<string> skipped)
        {
            if (target == null)
            {
                skipped.Add(label + " (method not found)");
                return;
            }

            try
            {
                harmony.Patch(target,
                    prefix != null ? new HarmonyMethod(typeof(GamePatches), prefix) : null,
                    postfix != null ? new HarmonyMethod(typeof(GamePatches), postfix) : null);
                applied.Add(label);
            }
            catch (Exception e)
            {
                skipped.Add(label + " (" + e.GetType().Name + ": " + e.Message + ")");
            }
        }

        private static bool HideDlss(ref bool __result)
        {
            if (!Settings.DisableDlss.Value || !UuvrCompat.Installed) return true;
            __result = false;
            Log.InfoOnce("dlss", "DLSS removed from the graphics options because it breaks VR rendering. A saved DLSS setting falls back to 'None'.");
            return false;
        }

        private static bool HideFsr3(ref bool __result)
        {
            if (!Settings.DisableFsr3.Value || !UuvrCompat.Installed) return true;
            __result = false;
            Log.InfoOnce("fsr3", "FSR3 removed from the graphics options because it is not built for VR rendering.");
            return false;
        }

        private static void RedirectIntroVideo(MonoBehaviour __instance)
        {
            if (!Settings.FixIntroVideo.Value || !XrState.IsActive) return;
            IntroVideo.Redirect(__instance.GetComponent<VideoPlayer>());
        }

        private static void HideHardwareCursorGraphic(ref bool showTexture)
        {
            if (XrState.IsActive && Settings.HideHardwareCursor.Value && Settings.ShowVrCursor.Value)
            {
                showTexture = false;
            }
        }

        private static bool RemapWorldToViewportMono(Camera __instance, Vector3 position, ref Vector3 __result)
        {
            return RemapWorldToViewport(__instance, position, Camera.MonoOrStereoscopicEye.Mono, ref __result);
        }

        private static bool RemapWorldToViewport(Camera __instance, Vector3 position, Camera.MonoOrStereoscopicEye eye, ref Vector3 __result)
        {
            Vector3 remapped;
            if (!MarkerAlignment.TryRemap(__instance, position, eye, out remapped)) return true;
            __result = remapped;
            return false;
        }
    }
}
