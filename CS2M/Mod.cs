using System.Reflection;
using Colossal.IO.AssetDatabase;
using CS2M.Commands;
using CS2M.Commands.ApiServer;
using CS2M.Mods;
using CS2M.Networking;
using CS2M.Settings;
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

            // Register the UI system so the CS2M button appears on the main menu and
            // the multiplayer screens (join / host / hub) can be opened. The actual
            // mod support / networking / harmony patches are intentionally skipped in
            // this load path: registering ECS systems on SystemUpdatePhase.GameSimulation
            // plus PatchAll(Assembly.GetExecutingAssembly()) was triggering a Unity
            // ECS hang (error.typeHang) ~30s after the game entered the main menu, even
            // though every system OnUpdate returns early without an active session.
            // Until the cooperative multiplayer server exists there is nothing for the
            // BaseGame sync systems to do anyway; the UI is the only thing that
            // currently has a usable surface.
            updateSystem.UpdateAt<UISystem>(SystemUpdatePhase.UIUpdate);

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
