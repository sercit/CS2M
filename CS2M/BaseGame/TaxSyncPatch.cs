using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.Networking;
using Game.Economy;
using Game.Simulation;
using HarmonyLib;

namespace CS2M.BaseGame
{
    // CSM-style event-driven tax sync: each TaxSystem setter is patched with a
    // Postfix that immediately broadcasts the change to all peers.
    // ReplayScope.IsReplayActive prevents re-entry when the receiver applies the command.

    [HarmonyPatch(typeof(TaxSystem), "SetTaxRate")]
    [HarmonyPatch(new[] { typeof(TaxAreaType), typeof(int) })]
    public static class TaxAreaRatePatch
    {
        public static void Postfix(TaxAreaType areaType, int rate)
        {
            if (ReplayScope.IsReplayActive) return;
            if (NetworkInterface.Instance?.LocalPlayer?.PlayerStatus != CS2M.API.Networking.PlayerStatus.PLAYING) return;
            Log.Info($"TaxAreaRatePatch: sending SetTaxRate({areaType}, {rate}).");
            Command.SendToAll(new TaxRateSyncCommand { SetterMethod = 0, Param1 = (int)areaType, Rate = rate });
        }
    }

    [HarmonyPatch(typeof(TaxSystem), "SetResidentialTaxRate")]
    [HarmonyPatch(new[] { typeof(int), typeof(int) })]
    public static class TaxResidentialRatePatch
    {
        public static void Postfix(int jobLevel, int rate)
        {
            if (ReplayScope.IsReplayActive) return;
            if (NetworkInterface.Instance?.LocalPlayer?.PlayerStatus != CS2M.API.Networking.PlayerStatus.PLAYING) return;
            Log.Info($"TaxResidentialRatePatch: sending SetResidentialTaxRate(level={jobLevel}, {rate}).");
            Command.SendToAll(new TaxRateSyncCommand { SetterMethod = 1, Param1 = jobLevel, Rate = rate });
        }
    }

    [HarmonyPatch(typeof(TaxSystem), "SetCommercialTaxRate")]
    [HarmonyPatch(new[] { typeof(Resource), typeof(int) })]
    public static class TaxCommercialRatePatch
    {
        public static void Postfix(Resource resource, int rate)
        {
            if (ReplayScope.IsReplayActive) return;
            if (NetworkInterface.Instance?.LocalPlayer?.PlayerStatus != CS2M.API.Networking.PlayerStatus.PLAYING) return;
            Log.Info($"TaxCommercialRatePatch: sending SetCommercialTaxRate({resource}, {rate}).");
            Command.SendToAll(new TaxRateSyncCommand { SetterMethod = 2, Param1 = (int)resource, Rate = rate });
        }
    }

    [HarmonyPatch(typeof(TaxSystem), "SetIndustrialTaxRate")]
    [HarmonyPatch(new[] { typeof(Resource), typeof(int) })]
    public static class TaxIndustrialRatePatch
    {
        public static void Postfix(Resource resource, int rate)
        {
            if (ReplayScope.IsReplayActive) return;
            if (NetworkInterface.Instance?.LocalPlayer?.PlayerStatus != CS2M.API.Networking.PlayerStatus.PLAYING) return;
            Log.Info($"TaxIndustrialRatePatch: sending SetIndustrialTaxRate({resource}, {rate}).");
            Command.SendToAll(new TaxRateSyncCommand { SetterMethod = 3, Param1 = (int)resource, Rate = rate });
        }
    }

    [HarmonyPatch(typeof(TaxSystem), "SetOfficeTaxRate")]
    [HarmonyPatch(new[] { typeof(Resource), typeof(int) })]
    public static class TaxOfficeRatePatch
    {
        public static void Postfix(Resource resource, int rate)
        {
            if (ReplayScope.IsReplayActive) return;
            if (NetworkInterface.Instance?.LocalPlayer?.PlayerStatus != CS2M.API.Networking.PlayerStatus.PLAYING) return;
            Log.Info($"TaxOfficeRatePatch: sending SetOfficeTaxRate({resource}, {rate}).");
            Command.SendToAll(new TaxRateSyncCommand { SetterMethod = 4, Param1 = (int)resource, Rate = rate });
        }
    }
}
