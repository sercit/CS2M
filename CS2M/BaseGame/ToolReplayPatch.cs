using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.Commands.Handler.BaseGame;
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
            if (Command.CurrentRole == MultiplayerRole.Client)
            {
                // Drain server-replicated roads onto this client.
                while (RoadSyncService.PendingReplays.TryDequeue(out RoadApplyCommand command))
                {
                    Log.Info($"NetToolReplayPatch: replaying road nonce={command.ApplyNonce}, prefab={command.PrefabName}");
                    bool ok = RoadSyncService.TryReplayApply(command);
                    if (!ok)
                        Log.Warn($"NetToolReplayPatch: failed to replay road nonce={command.ApplyNonce}");
                }
            }
            else if (Command.CurrentRole == MultiplayerRole.Server)
            {
                // Drain client road requests — must run inside NetToolSystem.OnUpdate so
                // SafeCommandBufferSystem.CreateCommandBuffer() is available.
                while (RoadSyncService.PendingServerRequests.TryDequeue(out RoadApplyCommand command))
                {
                    Log.Info($"NetToolReplayPatch: applying client road request nonce={command.ApplyNonce}, prefab={command.PrefabName}");
                    RoadApplyCommandHandler.ApplyPendingServerRequest(command);
                }
            }
        }
    }

    [HarmonyPatch(typeof(ZoneToolSystem), "OnUpdate")]
    public static class ZoneToolReplayPatch
    {
        public static void Prefix(ZoneToolSystem __instance)
        {
            if (Command.CurrentRole == MultiplayerRole.Client)
            {
                while (ZoneSyncService.PendingReplays.TryDequeue(out ZoneApplyCommand command))
                {
                    Log.Info($"ZoneToolReplayPatch: replaying zone nonce={command.ApplyNonce}, prefab={command.PrefabName}");
                    bool ok = ZoneSyncService.TryReplayApply(command);
                    if (!ok)
                        Log.Warn($"ZoneToolReplayPatch: failed to replay zone nonce={command.ApplyNonce}");
                }
            }
            else if (Command.CurrentRole == MultiplayerRole.Server)
            {
                while (ZoneSyncService.PendingServerRequests.TryDequeue(out ZoneApplyCommand command))
                {
                    Log.Info($"ZoneToolReplayPatch: applying client zone request nonce={command.ApplyNonce}, prefab={command.PrefabName}");
                    ZoneApplyCommandHandler.ApplyPendingServerRequest(command);
                }
            }
        }
    }

    [HarmonyPatch(typeof(AreaToolSystem), "OnUpdate")]
    public static class AreaToolReplayPatch
    {
        public static void Prefix(AreaToolSystem __instance)
        {
            if (Command.CurrentRole == MultiplayerRole.Client)
            {
                while (AreaSyncService.PendingReplays.TryDequeue(out AreaApplyCommand command))
                {
                    Log.Info($"AreaToolReplayPatch: replaying area nonce={command.ApplyNonce}, prefab={command.PrefabName}");
                    bool ok = AreaSyncService.TryReplayApply(command);
                    if (!ok)
                        Log.Warn($"AreaToolReplayPatch: failed to replay area nonce={command.ApplyNonce}");
                }
            }
            else if (Command.CurrentRole == MultiplayerRole.Server)
            {
                while (AreaSyncService.PendingServerRequests.TryDequeue(out AreaApplyCommand command))
                {
                    Log.Info($"AreaToolReplayPatch: applying client area request nonce={command.ApplyNonce}, prefab={command.PrefabName}");
                    AreaApplyCommandHandler.ApplyPendingServerRequest(command);
                }
            }
        }
    }
}
