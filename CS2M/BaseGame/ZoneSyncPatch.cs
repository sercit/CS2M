using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.Commands.Handler.BaseGame;
using CS2M.Networking;
using Game.Tools;
using HarmonyLib;
using Unity.Jobs;

namespace CS2M.BaseGame
{
    [HarmonyPatch(typeof(ZoneToolSystem), "Apply")]
    [HarmonyPatch(new[] { typeof(JobHandle), typeof(bool) })]
    public static class ZoneSyncPatch
    {
        public static bool Prefix(
            ZoneToolSystem __instance,
            JobHandle inputDeps,
            bool singleFrameOnly,
            ref JobHandle __result,
            out ZoneApplyCommand __state)
        {
            __state = null;

            if (ReplayScope.IsReplayActive || !ShouldHandle(__instance))
            {
                return true;
            }

            ZoneSyncService.TryBuildApplyCommand(__instance, singleFrameOnly, requestOnly: false, out __state);

            if (Command.CurrentRole == MultiplayerRole.Client && __state != null)
            {
                ZoneApplyCommandHandler.PreMarkSentNonce(__state.ApplyNonce);
            }

            return true;
        }

        public static void Postfix(ZoneApplyCommand __state)
        {
            if (ReplayScope.IsReplayActive || __state == null)
            {
                return;
            }

            if (Command.CurrentRole == MultiplayerRole.Client)
            {
                Command.SendToServer?.Invoke(__state);
                Log.Info($"ZoneSyncPatch: sent built zone to server, nonce={__state.ApplyNonce}, prefab={__state.PrefabName}.");
            }
            else if (Command.CurrentRole == MultiplayerRole.Server)
            {
                Command.SendToClients?.Invoke(__state);
                Log.Info($"ZoneSyncPatch: sent zone to clients, nonce={__state.ApplyNonce}.");
            }
        }

        private static bool ShouldHandle(ZoneToolSystem toolSystem)
        {
            if (toolSystem == null)
            {
                return false;
            }

            if (NetworkInterface.Instance?.LocalPlayer?.PlayerStatus != CS2M.API.Networking.PlayerStatus.PLAYING)
            {
                return false;
            }

            return Command.CurrentRole == MultiplayerRole.Client || Command.CurrentRole == MultiplayerRole.Server;
        }
    }
}
