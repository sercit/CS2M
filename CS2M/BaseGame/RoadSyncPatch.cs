using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
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

            if (Command.CurrentRole == MultiplayerRole.Client)
            {
                if (!RoadSyncService.IsSupportedOperation(__instance, out string reason))
                {
                    Log.Warn($"RoadSyncPatch: unsupported net action on client ({reason}), blocking.");
                    __result = inputDeps;
                    return false;
                }

                bool built = RoadSyncService.TryBuildApplyCommand(
                    __instance,
                    singleFrameOnly,
                    requestOnly: true,
                    out RoadApplyCommand request);
                if (!built)
                {
                    Log.Warn("RoadSyncPatch: failed to build road request command.");
                    __result = inputDeps;
                    return false;
                }

                Command.SendToServer?.Invoke(request);
                Log.Debug($"RoadSyncPatch: sent road request nonce {request.ApplyNonce}.");
                __result = inputDeps;
                return false;
            }

            if (Command.CurrentRole == MultiplayerRole.Server)
            {
                // Always let Apply run on server — replication is best-effort
                RoadSyncService.TryBuildApplyCommand(
                    __instance,
                    singleFrameOnly,
                    requestOnly: false,
                    out __state);
            }

            return true;
        }

        public static void Postfix(RoadApplyCommand __state)
        {
            if (ReplayScope.IsReplayActive || Command.CurrentRole != MultiplayerRole.Server || __state == null)
            {
                return;
            }

            Command.SendToClients?.Invoke(__state);
            Log.Info($"RoadSyncPatch: sent road to clients, nonce={__state.ApplyNonce}, prefab={__state.PrefabName}.");
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
                Log.Info($"RoadSyncPatch.ShouldHandle: blocked, PlayerStatus={status}, role={Command.CurrentRole}");
                return false;
            }

            return Command.CurrentRole == MultiplayerRole.Client || Command.CurrentRole == MultiplayerRole.Server;
        }
    }
}
