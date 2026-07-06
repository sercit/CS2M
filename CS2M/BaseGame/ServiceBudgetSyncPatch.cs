using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.Networking;
using Game.UI.InGame;
using HarmonyLib;
using Unity.Entities;

namespace CS2M.BaseGame
{
    /// <summary>
    /// Patches ServiceBudgetUISystem.SetServiceBudget so that any budget slider
    /// change is immediately replicated to all peers.
    /// </summary>
    [HarmonyPatch(typeof(ServiceBudgetUISystem), "SetServiceBudget")]
    [HarmonyPatch(new[] { typeof(Entity), typeof(int) })]
    public static class ServiceBudgetSyncPatch
    {
        public static void Postfix(Entity service, int percentage)
        {
            if (ReplayScope.IsReplayActive)
            {
                return;
            }

            var status = NetworkInterface.Instance?.LocalPlayer?.PlayerStatus;
            if (status != CS2M.API.Networking.PlayerStatus.PLAYING)
            {
                return;
            }

            Log.Info($"ServiceBudgetSyncPatch: sending budget entity={service.Index}:{service.Version} → {percentage}%.");
            Command.SendToAll(new ServiceBudgetSyncCommand
            {
                ServiceEntityIndex = service.Index,
                ServiceEntityVersion = service.Version,
                Percentage = percentage
            });
        }
    }
}
