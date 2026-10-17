using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Turrets aim where you look. The game's turret camera is mounted on the turret, and the mouse turns the turret,
    /// camera and all. Turning the turret to where you look would turn the camera with it, and so where you look: the
    /// turret would chase your view forever. So while you aim with your head in a turret, the camera keeps a view
    /// direction of its own (it moves with the ship, and the mouse turns it, the way the mouse turns the turret), and
    /// every frame the turret is turned through the game's own turret controls to the point you look at through the
    /// crosshair in the middle of your view. The turret's limits still apply, and other players see it turn as usual.
    /// </summary>
    internal static class TurretAim
    {
        // The turret is pointed at the spot this far along your line of sight, far enough to be parallel to it.
        private const float AimDistance = 1000f;
        private const float MaxPitch = 85f;

        private static bool _resolved;
        private static bool _broken;
        private static PropertyInfo _chairProperty;
        private static PropertyInfo _allowInputProperty;
        private static PropertyInfo _turretProperty;
        private static MethodInfo _sendInput;
        private static object _aimMove;
        private static PropertyInfo _outsideProperty;
        private static FieldInfo _pivotField;
        private static FieldInfo _scopeField;
        private static FieldInfo _zoomField;
        private static MethodInfo _deltaToTarget;
        private static FieldInfo _feedCameraField;
        private static PropertyInfo _playerInputProperty;
        private static MethodInfo _lookVector;
        private static readonly object[] NotSmoothed = { false };
        private static readonly object[] TargetArgument = new object[1];
        private static readonly object[] InputArguments = new object[2];

        // The local player's turret controls (an Opsive ability, a plain class) and the frame they last ran in.
        private static object _ability;
        private static int _abilityFrame = -10;
        private static Component _pivot;
        private static Quaternion _frame = Quaternion.identity;
        private static float _yaw;
        private static float _pitch;

        /// <summary>Set once both game hooks (the turret controls and the camera update) are in place.</summary>
        public static bool Hooked { get; set; }

        /// <summary>True while the turret aims where you look (decided once per frame by HeadAim.Update).</summary>
        public static bool Steering { get; private set; }

        /// <summary>True while the local player controls a turret (its controls ran this frame or the one before).</summary>
        public static bool InTurret
        {
            get { return _ability != null && Time.frameCount - _abilityFrame <= 2; }
        }

        public static void SetSteering(bool steering)
        {
            steering = steering && Hooked && InTurret && Resolve();
            if (steering == Steering) return;
            Steering = steering;
            if (steering) Log.InfoOnce("turret-aim", "Turrets aim where you look: the mouse turns your view and the turret follows the crosshair.");
            else Log.Verbose("The turret is aimed with the mouse again.");
        }

        public static void Stop()
        {
            Steering = false;
            _ability = null;
            _pivot = null;
        }

        public static string Describe()
        {
            if (!Hooked) return "unavailable (the game's turret controls could not be patched), turrets aim with the mouse";
            if (_broken) return "unavailable (turret internals not found), turrets aim with the mouse";
            if (!InTurret) return "not in a turret";
            if (!Steering) return "turret aimed with the mouse (" + (Settings.AimWithHead.Value ? "a menu is open" : "Aim With Your Head is off") + ")";
            return string.Format("the turret follows your view; view turned {0:0}° and tilted {1:0}° from the turret's mount", _yaw, _pitch);
        }

        /// <summary>
        /// The local player's turret controls, once per frame while in a turret. Returns true when they are taken over,
        /// so the mouse doesn't turn the turret itself.
        /// </summary>
        public static bool TakeOverInput(object ability)
        {
            // Sat down (again): start from the turret's own view.
            if (!ReferenceEquals(ability, _ability) || Time.frameCount - _abilityFrame > 2) _pivot = null;
            _ability = ability;
            _abilityFrame = Time.frameCount;
            return Steering;
        }

        /// <summary>
        /// Right after the game has placed its camera for this frame: the camera gets your view direction, and the
        /// turret is turned to where you look through the crosshair.
        /// </summary>
        public static void AfterCameraUpdate(CinemachineBrainUpdater updater)
        {
            if (!Steering || _broken || updater == null) return;
            try
            {
                Steer(updater.GetCamera());
            }
            catch (Exception e)
            {
                _broken = true;
                Steering = false;
                Log.Failure("Aiming the turret with your head (turrets are aimed with the mouse for the rest of this session)", e);
            }
        }

        private static void Steer(Camera camera)
        {
            // The game's own flag goes off the moment you get up, before the controls stop running.
            if (camera == null || _ability == null || !PlayerModes.InTurret()) return;
            object chair = _chairProperty.GetValue(_ability, null);
            object turret = chair != null ? _turretProperty.GetValue(chair, null) : null;
            object outside = turret != null ? _outsideProperty.GetValue(turret, null) : null;
            var pivot = outside != null ? _pivotField.GetValue(outside) as Component : null;
            var scope = outside != null ? _scopeField.GetValue(outside) as Component : null;
            var feedCamera = scope != null ? _feedCameraField.GetValue(scope) as Component : null;
            Transform mount = pivot != null ? pivot.transform.parent : null;
            if (mount == null || feedCamera == null) return;

            if (!ReferenceEquals(pivot, _pivot))
            {
                // Just sat down, or switched to the other barrel: start from where the turret's own camera looks. The view
                // turns around the mount's up (upside-down turrets have their camera turned the right way up).
                _pivot = pivot;
                Transform feed = feedCamera.transform;
                _frame = Vector3.Dot(feed.up, mount.up) < 0f ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
                Vector3 forward = Quaternion.Inverse(mount.rotation * _frame) * feed.forward;
                _yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                _pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg, -MaxPitch, MaxPitch);
                Log.Verbose(string.Format("Turret view starts {0:0}° turned and {1:0}° tilted from the turret's mount.", _yaw, _pitch));
            }

            // The mouse turns the view like it turns the turret: same amounts, and slower while zoomed in.
            float zoom = (float)_zoomField.GetValue(pivot);
            UnityEngine.Object player = PlayerModes.LocalPlayer();
            object input = player != null ? _playerInputProperty.GetValue(player, null) : null;
            if (input != null)
            {
                var look = (Vector2)_lookVector.Invoke(input, NotSmoothed);
                _yaw = Mathf.Repeat(_yaw + look.x * zoom + 180f, 360f) - 180f;
                _pitch = Mathf.Clamp(_pitch - look.y * zoom, -MaxPitch, MaxPitch);
            }

            Transform cameraTransform = camera.transform;
            cameraTransform.rotation = mount.rotation * _frame * Quaternion.Euler(0f, _yaw, 0f) * Quaternion.Euler(_pitch, 0f, 0f);

            if (!(bool)_allowInputProperty.GetValue(_ability, null)) return;

            // Turned by exactly the angle to where you look; the game scales turret input down while zoomed in.
            Quaternion aim = HeadAim.AimRotation(camera);
            TargetArgument[0] = cameraTransform.position + aim * Vector3.forward * AimDistance;
            var delta = (Vector2)_deltaToTarget.Invoke(pivot, TargetArgument);
            if (delta.sqrMagnitude < 1e-6f) return;
            InputArguments[0] = _aimMove;
            InputArguments[1] = zoom > 0.001f ? delta / zoom : delta;
            _sendInput.Invoke(chair, InputArguments);
        }

        /// <summary>Looks up the game's turret classes by name (they are networked, so not referenced directly).</summary>
        private static bool Resolve()
        {
            if (_resolved) return !_broken;
            _resolved = true;

            var missing = new List<string>();
            try
            {
                Type ability = AccessTools.TypeByName("ControllingTurret");
                Type chair = AccessTools.TypeByName("TurretChair");
                Type module = AccessTools.TypeByName("Gameplay.CompositeWeapons.CompositeWeaponModule");
                Type outside = module != null ? module.GetNestedType("CompositeWeaponOutsideElements", BindingFlags.Public | BindingFlags.NonPublic) : null;
                Type pivot = AccessTools.TypeByName("Gameplay.CompositeWeapons.WeaponPivot");
                Type scope = AccessTools.TypeByName("Gameplay.CompositeWeapons.WeaponScope");
                Type actions = AccessTools.TypeByName("Gameplay.CompositeWeapons.WeaponActionType");
                Type player = AccessTools.TypeByName("CG.Game.Player.LocalPlayer");

                _chairProperty = Find(ability != null ? AccessTools.Property(ability, "TurretChair") : null, "ControllingTurret.TurretChair", missing);
                _allowInputProperty = Find(ability != null ? AccessTools.Property(ability, "AllowTurretInput") : null, "ControllingTurret.AllowTurretInput", missing);
                _turretProperty = Find(chair != null ? AccessTools.Property(chair, "Turret") : null, "TurretChair.Turret", missing);
                _sendInput = Find(chair != null && actions != null ? AccessTools.Method(chair, "SendInput", new[] { actions, typeof(object) }) : null, "TurretChair.SendInput", missing);
                _aimMove = actions != null && Enum.IsDefined(actions, "AimMove") ? Enum.Parse(actions, "AimMove") : null;
                if (_aimMove == null) missing.Add("WeaponActionType.AimMove");
                _outsideProperty = Find(module != null ? AccessTools.Property(module, "ActiveOutsideElementCollection") : null, "CompositeWeaponModule.ActiveOutsideElementCollection", missing);
                _pivotField = Find(outside != null ? AccessTools.Field(outside, "Pivot") : null, "CompositeWeaponOutsideElements.Pivot", missing);
                _scopeField = Find(outside != null ? AccessTools.Field(outside, "Scope") : null, "CompositeWeaponOutsideElements.Scope", missing);
                _zoomField = Find(pivot != null ? AccessTools.Field(pivot, "RotationZoomModifier") : null, "WeaponPivot.RotationZoomModifier", missing);
                _deltaToTarget = Find(pivot != null ? AccessTools.Method(pivot, "GetPerspectiveDeltaToTarget", new[] { typeof(Vector3) }) : null,
                    "WeaponPivot.GetPerspectiveDeltaToTarget", missing);
                _feedCameraField = Find(scope != null ? AccessTools.Field(scope, "ControlFeedCamera") : null, "WeaponScope.ControlFeedCamera", missing);
                _playerInputProperty = Find(player != null ? AccessTools.Property(player, "PlayerInput") : null, "LocalPlayer.PlayerInput", missing);
                _lookVector = Find(_playerInputProperty != null ? AccessTools.Method(_playerInputProperty.PropertyType, "GetLookVector", new[] { typeof(bool) }) : null,
                    "PlayerInput.GetLookVector", missing);

                if (_allowInputProperty != null && _allowInputProperty.PropertyType != typeof(bool)) missing.Add("ControllingTurret.AllowTurretInput (not a bool)");
                if (_zoomField != null && _zoomField.FieldType != typeof(float)) missing.Add("WeaponPivot.RotationZoomModifier (not a float)");
                if (_deltaToTarget != null && _deltaToTarget.ReturnType != typeof(Vector2)) missing.Add("WeaponPivot.GetPerspectiveDeltaToTarget (not a Vector2)");
                if (_lookVector != null && _lookVector.ReturnType != typeof(Vector2)) missing.Add("PlayerInput.GetLookVector (not a Vector2)");
            }
            catch (Exception e)
            {
                missing.Add(e.GetType().Name + ": " + e.Message);
            }

            if (missing.Count > 0)
            {
                _broken = true;
                Log.Warn("Turrets can't aim where you look, so they are aimed with the mouse: the game's " + string.Join(", ", missing.ToArray()) +
                         " was not found (the game may have been updated).");
            }
            return !_broken;
        }

        private static T Find<T>(T member, string name, List<string> missing) where T : MemberInfo
        {
            if (member == null) missing.Add(name);
            return member;
        }
    }
}
