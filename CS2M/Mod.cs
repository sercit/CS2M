using System.Reflection;
using Colossal.IO.AssetDatabase;
using CS2M.BaseGame.Systems;
using CS2M.Commands;
using CS2M.Commands.ApiServer;
using CS2M.Helpers;
using CS2M.Mods;
using CS2M.Networking;
using CS2M.Settings;
using CS2M.Systems;
using CS2M.UI;
using Game;
using Game.Modding;
using HarmonyLib;
using LiteNetLib;

namespace CS2M
{
    /// <summary>
    ///     The base mod class for instantiation by the game.
    /// </summary>
    public class Mod : IMod
    {
        private const string HarmonyPatchID = "com.citiesskylinesmultiplayer";

        /// <summary>
        ///     The mod's default name.
        /// </summary>
        public const string Name = nameof(CS2M);

        /// <summary>
        ///     Gets the active instance reference.
        /// </summary>
        public static Mod Instance { get; private set; }

        /// <summary>
        ///     Gets the mod's active settings configuration.
        /// </summary>
        internal ModSettings Settings { get; private set; }

        /// <summary>
        ///     Called by the game when the mod is loaded.
        /// </summary>
        /// <param name="updateSystem">Game update system.</param>
        public void OnLoad(UpdateSystem updateSystem)
        {
            // Set instance reference.
            Instance = this;
            Log.Initialize();
            Log.Info($"Loading {Name} version {Assembly.GetExecutingAssembly().GetName().Version}");

            // Register mod settings to game options UI.
            Log.Info("Loading Mod Settings");
            Settings = new ModSettings(this);
            Settings.RegisterInOptionsUI();

            // Load saved settings.
            AssetDatabase.global.LoadSettings(Name, Settings, new ModSettings(this));
            Settings.OnSetLoggingLevel(Settings.LoggingLevel);
            Log.Info("Configured and initialised mod settings");

            // Initialise the command serialisation pipeline. These are plain-object
            // singletons (no ECS, no Harmony) and must be ready before any network
            // call fires (e.g. ConnectionEstablished → SendToServer).
            // RefreshModel() is deferred to first use (called from UISystem.OnCreate
            // via EnsureCommandModel) so that ECS World is fully ready by then.
            CommandInternal.Instance = new CommandInternal();
            ApiCommand.Instance = new ApiCommand();

            // Apply only the Harmony patches required for multiplayer:
            //   - Two patches for save/load interop (fire only during GameManager.Load).
            //   - Five targeted tool patches that intercept Apply() on each tool system so
            //     the host can replicate actions to clients and clients can send requests
            //     to the server.  These are class-processor patches (not PatchAll) so they
            //     don't trigger the ECS typeHang observed with broad patching.
            var harmony = new Harmony(HarmonyPatchID);
            harmony.CreateClassProcessor(typeof(CS2M.Helpers.AssetDataPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CS2M.Helpers.ReadSystemPatch)).Patch();

            // Tool-interception patches (CS2M project, CS2M.BaseGame namespace).
            harmony.CreateClassProcessor(typeof(CS2M.BaseGame.BuildingPlacementPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CS2M.BaseGame.RoadSyncPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CS2M.BaseGame.BulldozePatch)).Patch();
            harmony.CreateClassProcessor(typeof(CS2M.BaseGame.ZoneSyncPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CS2M.BaseGame.AreaSyncPatch)).Patch();

            // Client-side replay patches: drain pending commands inside each tool
            // system's own OnUpdate so that SafeCommandBufferSystem is available.
            harmony.CreateClassProcessor(typeof(CS2M.BaseGame.NetToolReplayPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CS2M.BaseGame.ZoneToolReplayPatch)).Patch();
            harmony.CreateClassProcessor(typeof(CS2M.BaseGame.AreaToolReplayPatch)).Patch();

            // Register the UI system so the CS2M button appears on the main menu and
            // the multiplayer screens (join / host / hub) can be opened.
            updateSystem.UpdateAt<UISystem>(SystemUpdatePhase.UIUpdate);

            // Register ECS synchronisation systems.  These are safe to add alongside
            // the targeted Harmony patches because they don't use PatchAll.
            // CS2M.BaseGame sync systems (authoritative frame/money/time/XP broadcast).
            updateSystem.UpdateAt<FrameSyncSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<MoneySyncSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<CS2M.BaseGame.ActionReplaySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<TimeSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<XPMilestoneSyncSystem>(SystemUpdatePhase.GameSimulation);

            // Cooperative overlay system (cursor tracking, activity log, map pings).
            updateSystem.UpdateAt<CooperativeSyncSystem>(SystemUpdatePhase.UIUpdate);

            Log.Info("Loading complete");
        }

        public void OnDispose()
        {
            //new Harmony(HarmonyPatchID).UnpatchAll(HarmonyPatchID);

            ModSupport.Instance.DestroyConnections();

            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
