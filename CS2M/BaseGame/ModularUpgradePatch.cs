using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.Helpers;
using CS2M.Networking;
using Game.Prefabs;
using Game.Tools;
using HarmonyLib;
using Unity.Entities;
using Unity.Jobs;

namespace CS2M.BaseGame
{
    /// <summary>
    /// Intercepts ObjectToolSystem.Apply when the active prefab is a modular service
    /// upgrade (not a plain BuildingPrefab — that case is handled by BuildingPlacementPatch).
    /// Captures the upgrade prefab and parent building entity and replicates across the
    /// multiplayer session.
    /// </summary>
    [HarmonyPatch(typeof(ObjectToolSystem), "Apply")]
    [HarmonyPatch(new[] { typeof(JobHandle), typeof(bool) })]
    public static class ModularUpgradePatch
    {
        public static bool Prefix(
            ObjectToolSystem __instance,
            JobHandle inputDeps,
            bool singleFrameOnly,
            ref JobHandle __result,
            out ModularUpgradeCommand __state)
        {
            __state = null;

            if (ReplayScope.IsReplayActive || !ShouldHandle(__instance))
            {
                return true;
            }

            if (Command.CurrentRole == MultiplayerRole.Client)
            {
                if (TryBuildCommand(__instance, requestOnly: true, out ModularUpgradeCommand request))
                {
                    Command.SendToServer?.Invoke(request);
                    Log.Info($"ModularUpgradePatch: sent upgrade request '{request.UpgradePrefabName}' nonce={request.UpgradeNonce}.");
                }

                __result = inputDeps;
                return false;
            }

            if (Command.CurrentRole == MultiplayerRole.Server)
            {
                TryBuildCommand(__instance, requestOnly: false, out __state);
            }

            return true;
        }

        public static void Postfix(ModularUpgradeCommand __state)
        {
            if (ReplayScope.IsReplayActive || Command.CurrentRole != MultiplayerRole.Server || __state == null)
            {
                return;
            }

            Command.SendToClients?.Invoke(__state);
            Log.Info($"ModularUpgradePatch: replicated upgrade '{__state.UpgradePrefabName}' nonce={__state.UpgradeNonce} to clients.");
        }

        private static bool ShouldHandle(ObjectToolSystem tool)
        {
            if (tool?.prefab == null)
            {
                return false;
            }

            var status = NetworkInterface.Instance?.LocalPlayer?.PlayerStatus;
            if (status != CS2M.API.Networking.PlayerStatus.PLAYING)
            {
                return false;
            }

            if (Command.CurrentRole != MultiplayerRole.Client && Command.CurrentRole != MultiplayerRole.Server)
            {
                return false;
            }

            if (tool.state != ObjectToolSystem.State.Adding)
            {
                return false;
            }

            // Plain building placement is handled by BuildingPlacementPatch
            if (tool.mode == ObjectToolSystem.Mode.Create && tool.prefab is BuildingPrefab)
            {
                return false;
            }

            // Only handle service upgrades / modular extensions (non-building prefabs)
            return !(tool.prefab is BuildingPrefab);
        }

        private static bool TryBuildCommand(ObjectToolSystem tool, bool requestOnly, out ModularUpgradeCommand command)
        {
            command = null;

            if (tool.prefab == null)
            {
                return false;
            }

            // Placement point
            ControlPoint lastRaycast = default;
            object lrObj = ReflectionHelper.GetAttr(tool, "m_LastRaycastPoint");
            if (lrObj is ControlPoint lrCp)
            {
                lastRaycast = lrCp;
            }

            // Parent building entity: try m_Owner field first, fall back to raycast original
            Entity parentEntity = Entity.Null;

            object ownerObj = ReflectionHelper.GetAttr(tool, "m_Owner");
            if (ownerObj is Entity ownerEnt && ownerEnt != Entity.Null)
            {
                parentEntity = ownerEnt;
            }
            else if (lastRaycast.m_OriginalEntity != Entity.Null)
            {
                parentEntity = lastRaycast.m_OriginalEntity;
            }

            if (parentEntity == Entity.Null)
            {
                Log.Warn($"ModularUpgradePatch: could not resolve parent entity for upgrade '{tool.prefab.name}'.");
            }

            command = new ModularUpgradeCommand
            {
                UpgradePrefabName = tool.prefab.name,
                ParentEntityIndex = parentEntity.Index,
                ParentEntityVersion = parentEntity.Version,
                PositionX = lastRaycast.m_Position.x,
                PositionY = lastRaycast.m_Position.y,
                PositionZ = lastRaycast.m_Position.z,
                RotationX = lastRaycast.m_Rotation.value.x,
                RotationY = lastRaycast.m_Rotation.value.y,
                RotationZ = lastRaycast.m_Rotation.value.z,
                RotationW = lastRaycast.m_Rotation.value.w,
                UpgradeNonce = ModularUpgradeService.NextUpgradeNonce(),
                RequestOnly = requestOnly
            };

            return true;
        }
    }
}
