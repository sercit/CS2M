using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.Commands.Handler.BaseGame;
using CS2M.Networking;
using Game.Tools;
using HarmonyLib;
using Unity.Jobs;

namespace CS2M.BaseGame
{
    [HarmonyPatch(typeof(NetToolSystem), "Apply")]
    [HarmonyPatch(new[] { typeof(JobHandle), typeof(bool) })]
    public static class RoadSyncPatch
    {
        public static bool Prefix(
            NetToolSystem __instance,
            JobHandle inputDeps,
            bool singleFrameOnly,
            ref JobHandle __result,
            out RoadApplyCommand __state)
        {
            __state = null;

            if (ReplayScope.IsReplayActive || !ShouldHandle(__instance))
            {
                return true;
            }

            // Capture tool state before Apply modifies it (used by both Client and Server postfix).
            RoadSyncService.TryBuildApplyCommand(__instance, singleFrameOnly, requestOnly: false, out __state);

            if (Command.CurrentRole == MultiplayerRole.Client && __state != null)
            {
                // Pre-mark nonce so when the server echoes this back we skip it.
                RoadApplyCommandHandler.PreMarkSentNonce(__state.ApplyNonce);
            }

            // Always let Apply run locally — client gets immediate feedback, server is authoritative.
            return true;
        }

        public static void Postfix(RoadApplyCommand __state)
        {
            if (ReplayScope.IsReplayActive || __state == null)
            {
                return;
            }

            if (Command.CurrentRole == MultiplayerRole.Client)
            {
                Command.SendToServer?.Invoke(__state);
                Log.Info($"RoadSyncPatch: sent built road to server, nonce={__state.ApplyNonce}, prefab={__state.PrefabName}.");
            }
            else if (Command.CurrentRole == MultiplayerRole.Server)
            {
                Command.SendToClients?.Invoke(__state);
                Log.Info($"RoadSyncPatch: sent road to clients, nonce={__state.ApplyNonce}, prefab={__state.PrefabName}.");
            }
        }

        private static bool ShouldHandle(NetToolSystem toolSystem)
        {
            if (toolSystem == null)
            {
                return false;
            }

            var status = NetworkInterface.Instance?.LocalPlayer?.PlayerStatus;
            if (status != CS2M.API.Networking.PlayerStatus.PLAYING)
            {
                return false;
            }

            return Command.CurrentRole == MultiplayerRole.Client || Command.CurrentRole == MultiplayerRole.Server;
        }
    }
}
