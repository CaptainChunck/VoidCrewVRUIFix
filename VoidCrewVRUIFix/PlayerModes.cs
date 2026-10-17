using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// What the local player is doing, from the game's interaction modes (walking, in a turret, flying the ship...).
    /// Read by name: the player class is networked, so the plugin doesn't reference it directly.
    /// </summary>
    internal static class PlayerModes
    {
        // Views where the mouse aims a turret or the ship's outside camera instead of the player. The game aims along
        // the camera's centre line there, where it draws its crosshair (the middle of the screen).
        private static readonly string[] VehicleViewModes = { "ControllingTurret", "ShipThirdPerson", "UsingPeriscope" };

        private static bool _resolved;
        private static bool _broken;
        private static PropertyInfo _localPlayer;
        private static PropertyInfo _interactionState;
        private static PropertyInfo _activeModes;
        private static long _vehicleViewMask;

        /// <summary>True while the player controls a turret or looks through the ship's outside cameras (third-person flying, periscope).</summary>
        public static bool InVehicleView()
        {
            long modes;
            return TryGetModes(out modes) && (modes & _vehicleViewMask) != 0;
        }

        /// <summary>The player's active interaction modes as flags, or false when there is no player (menus, loading).</summary>
        public static bool TryGetModes(out long modes)
        {
            modes = 0;
            if (_broken || !Resolve()) return false;
            try
            {
                var player = _localPlayer.GetValue(null, null) as UnityEngine.Object;
                if (player == null) return false;
                object state = _interactionState.GetValue(player, null);
                if (state == null) return false;
                modes = Convert.ToInt64(_activeModes.GetValue(state, null));
                return true;
            }
            catch (Exception e)
            {
                // Runs every frame: one report, then the plugin stops asking.
                _broken = true;
                Log.Failure("Reading the player's interaction mode (turrets keep head aim)", e);
                return false;
            }
        }

        /// <summary>For the diagnostics report: the active modes by name.</summary>
        public static string Describe()
        {
            long modes;
            if (!TryGetModes(out modes)) return _broken || (_resolved && _activeModes == null) ? "unknown (not found in this game version)" : "no player";
            var names = new List<string>();
            foreach (object value in Enum.GetValues(_activeModes.PropertyType))
            {
                long flag = Convert.ToInt64(value);
                if (flag != 0 && (modes & flag) == flag) names.Add(value.ToString());
            }
            return (names.Count > 0 ? string.Join(", ", names.ToArray()) : "none") + ((modes & _vehicleViewMask) != 0 ? " (turret or ship camera view)" : "");
        }

        private static bool Resolve()
        {
            if (_resolved) return _activeModes != null;
            _resolved = true;

            Type player = AccessTools.TypeByName("CG.Game.Player.LocalPlayer");
            Type state = AccessTools.TypeByName("CG.Client.Player.LocalPlayerInteractionState");
            _localPlayer = player != null ? AccessTools.Property(player, "I") : null;
            _interactionState = player != null ? AccessTools.Property(player, "InteractionState") : null;
            PropertyInfo activeModes = state != null ? AccessTools.Property(state, "ActiveModes") : null;
            if (_localPlayer == null || _interactionState == null || activeModes == null || !activeModes.PropertyType.IsEnum)
            {
                Log.Warn("The game's player interaction modes were not found (the game may have been updated); " +
                         "head aim and the UI screen stay as they are in turrets and the ship's outside cameras.");
                return false;
            }

            foreach (string name in VehicleViewModes)
            {
                if (Enum.IsDefined(activeModes.PropertyType, name)) _vehicleViewMask |= Convert.ToInt64(Enum.Parse(activeModes.PropertyType, name));
                else Log.Verbose("Interaction mode '" + name + "' not found in this game version.");
            }
            _activeModes = activeModes;
            return true;
        }
    }
}
