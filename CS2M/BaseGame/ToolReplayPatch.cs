using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.Networking;
using Game.Tools;
using HarmonyLib;

namespace CS2M.BaseGame
{
    /// <summary>
    /// Drains tool-specific pending replay queues from within the respective tool
    /// system's own ECS OnUpdate context.  This is the only place where
    /// SafeCommandBufferSystem.CreateCommandBuffer() is allowed — calling it from
    /// GameThreadDispatcher / ActionReplaySystem (GameSimulation phase) fails because
    /// the command-buffer barrier has not been opened for that phase.
    /// </summary>

    [HarmonyPatch(typeof(NetToolSystem), "OnUpdate")]
    public static class NetToolReplayPatch
    {
        public static void Prefix(NetToolSystem __instance)
        {
            if (Command.CurrentRole != MultiplayerRole.Client)
            {
                return;
            }

            while (RoadSyncService.PendingReplays.TryDequeue(out RoadApplyCommand command))
            {
                Log.Info($"NetToolReplayPatch: replaying road nonce={command.ApplyNonce}, prefab={command.PrefabName}");
                bool ok = RoadSyncService.TryReplayApply(command);
                if (!ok)
                {
                    Log.Warn($"NetToolReplayPatch: failed to replay road nonce={command.ApplyNonce}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(ZoneToolSystem), "OnUpdate")]
    public static class ZoneToolReplayPatch
    {
        public static void Prefix(ZoneToolSystem __instance)
        {
            if (Command.CurrentRole != MultiplayerRole.Client)
            {
                return;
            }

            while (ZoneSyncService.PendingReplays.TryDequeue(out ZoneApplyCommand command))
            {
                Log.Info($"ZoneToolReplayPatch: replaying zone nonce={command.ApplyNonce}, prefab={command.PrefabName}");
                bool ok = ZoneSyncService.TryReplayApply(command);
                if (!ok)
                {
                    Log.Warn($"ZoneToolReplayPatch: failed to replay zone nonce={command.ApplyNonce}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(AreaToolSystem), "OnUpdate")]
    public static class AreaToolReplayPatch
    {
        public static void Prefix(AreaToolSystem __instance)
        {
            if (Command.CurrentRole != MultiplayerRole.Client)
            {
                return;
            }

            while (AreaSyncService.PendingReplays.TryDequeue(out AreaApplyCommand command))
            {
                Log.Info($"AreaToolReplayPatch: replaying area nonce={command.ApplyNonce}, prefab={command.PrefabName}");
                bool ok = AreaSyncService.TryReplayApply(command);
                if (!ok)
                {
                    Log.Warn($"AreaToolReplayPatch: failed to replay area nonce={command.ApplyNonce}");
                }
            }
        }
    }
}
