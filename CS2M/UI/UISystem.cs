using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using CS2M.API.Networking;
using CS2M.Mods;
using CS2M.Networking;
using Game;
using Game.UI;
using Game.UI.InGame;

namespace CS2M.UI
{
    public partial class UISystem : UISystemBase
    {
        private ValueBinding<GameScreenUISystem.GameScreen> _activeGameScreenBinding;
        private ValueBinding<int> _activeMenuScreenBinding;
        private ValueBinding<int> _downloadDone;
        private ValueBinding<int> _downloadRemaining;
        private ValueBinding<int> _downloadSpeed;

        private GameMode _gameMode = GameMode.Other;
        private ValueBinding<bool> _hostMenuVisible;
        private ValueBinding<int> _hostPort;
        private ValueBinding<string> _hostPassword;

        private ValueBinding<string> _joinIPAddress;
        private ValueBinding<string> _joinToken;
        private ValueBinding<bool> _joinMenuVisible;
        private ValueBinding<bool> _hubMenuVisible;
        private ValueBinding<int> _joinPort;
        private ValueBinding<string> _joinPassword;
        private ValueBinding<List<string>> _joinErrorMessage;

        private ValueBinding<List<ModSupportStatus>> _modSupportStatus;
        private ValueBinding<string> _playerStatus;
        private ValueBinding<string> _playerType;

        private ValueBinding<string> _username;
        public static ValueBinding<string> CooperativeDataBinding;

        private readonly Stopwatch _downloadTimer = new();
        private int _lastDownloadDone = 0;

        private ChatPanel ChatPanel { get; } = new();

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            _activeMenuScreenBinding = BindingsHelper.GetValueBinding<int>("menu", "activeScreen");
            _activeGameScreenBinding =
                BindingsHelper.GetValueBinding<GameScreenUISystem.GameScreen>("game", "activeScreen");

            GamePanelUISystem gameChatPanel = World.GetOrCreateSystemManaged<GamePanelUISystem>();
            gameChatPanel.SetDefaultArgs(ChatPanel);
            ChatPanel.WelcomeChatMessage();
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            AddBinding(new TriggerBinding(Mod.Name, "ShowMultiplayerMenu", ShowMultiplayerMenu));
            AddBinding(new TriggerBinding(Mod.Name, "ShowJoinGameMenu", ShowJoinGameMenu));
            AddBinding(new TriggerBinding(Mod.Name, "ShowHostGameMenu", ShowHostGameMenu));
            AddBinding(new TriggerBinding(Mod.Name, "HideMultiplayerHub", HideMultiplayerHub));
            AddBinding(new TriggerBinding(Mod.Name, "HideJoinGameMenu", HideJoinGameMenu));
            AddBinding(new TriggerBinding(Mod.Name, "HideHostGameMenu", HideHostGameMenu));

            AddBinding(new TriggerBinding<string>(Mod.Name, "SetJoinIpAddress", ip => { _joinIPAddress.Update(ip); }));
            AddBinding(new TriggerBinding<string>(Mod.Name, "SetJoinToken", token => { _joinToken.Update(token); }));
            AddBinding(new TriggerBinding<int>(Mod.Name, "SetJoinPort", port => { _joinPort.Update(port); }));
            AddBinding(new TriggerBinding<string>(Mod.Name, "SetJoinPassword", password => { _joinPassword.Update(password); }));
            AddBinding(new TriggerBinding<int>(Mod.Name, "SetHostPort", port => { _hostPort.Update(port); }));
            AddBinding(new TriggerBinding<string>(Mod.Name, "SetHostPassword", password => { _hostPassword.Update(password); }));
            AddBinding(new TriggerBinding<string>(Mod.Name, "SetUsername",
                username => { _username.Update(username); }));

            AddBinding(new TriggerBinding(Mod.Name, "JoinGame", JoinGame));
            AddBinding(new TriggerBinding(Mod.Name, "HostGame", HostGame));
            AddBinding(new TriggerBinding(Mod.Name, "StopServer", StopServer));
            AddBinding(new TriggerBinding(Mod.Name, "LeaveSession", LeaveSession));

            AddBinding(_hubMenuVisible = new ValueBinding<bool>(Mod.Name, "HubMenuVisible", false));
            AddBinding(_joinMenuVisible = new ValueBinding<bool>(Mod.Name, "JoinMenuVisible", false));
            AddBinding(_hostMenuVisible = new ValueBinding<bool>(Mod.Name, "HostMenuVisible", false));
            AddBinding(_modSupportStatus = new ValueBinding<List<ModSupportStatus>>(Mod.Name, "modSupport",
                new List<ModSupportStatus>(), new ListWriter<ModSupportStatus>(new ValueWriter<ModSupportStatus>())));

            AddBinding(_joinIPAddress = new ValueBinding<string>(Mod.Name, "JoinIpAddress", ""));
            AddBinding(_joinToken = new ValueBinding<string>(Mod.Name, "JoinToken", ""));
            AddBinding(_joinPort = new ValueBinding<int>(Mod.Name, "JoinPort", 4230));
            AddBinding(_joinPassword = new ValueBinding<string>(Mod.Name, "JoinPassword", ""));
            AddBinding(_hostPort = new ValueBinding<int>(Mod.Name, "HostPort", 4230));
            AddBinding(_hostPassword = new ValueBinding<string>(Mod.Name, "HostPassword", ""));
            AddBinding(_username = new ValueBinding<string>(Mod.Name, "Username", ""));

            AddBinding(_playerStatus = new ValueBinding<string>(Mod.Name, "PlayerStatus", "INACTIVE"));
            AddBinding(_playerType = new ValueBinding<string>(Mod.Name, "PlayerType", "NONE"));
            AddBinding(_downloadDone = new ValueBinding<int>(Mod.Name, "DownloadDone", 0));
            AddBinding(_downloadRemaining = new ValueBinding<int>(Mod.Name, "DownloadRemaining", 0));
            AddBinding(_downloadSpeed = new ValueBinding<int>(Mod.Name, "DownloadSpeed", 0));
            AddBinding(_joinErrorMessage = new ValueBinding<List<string>>(Mod.Name, "JoinErrorMessage",
                new List<string>(), new ListWriter<string>()));

            AddBinding(CooperativeDataBinding = new ValueBinding<string>(Mod.Name, "CooperativeData", "{}"));
            AddBinding(new TriggerBinding<int>(Mod.Name, "TeleportToPlayer", playerId =>
            {
                CS2M.Systems.CooperativeSyncSystem.TeleportCameraToPlayer(playerId);
            }));
            AddBinding(new TriggerBinding<float, float, float>(Mod.Name, "TeleportToPosition", (x, y, z) =>
            {
                CS2M.Systems.CooperativeSyncSystem.TeleportCamera(new Unity.Mathematics.float3(x, y, z));
            }));

            _playerStatus.Update(NetworkInterface.Instance.LocalPlayer.PlayerStatus.ToString());
            _playerType.Update(NetworkInterface.Instance.LocalPlayer.PlayerType.ToString());
            string defaultUsername = NetworkInterface.Instance.LocalPlayer.Username;
            if (string.IsNullOrWhiteSpace(defaultUsername))
            {
                defaultUsername = Environment.UserName;
            }
            _username.Update(defaultUsername);
            NetworkInterface.Instance.UpdateLocalPlayerUsername(defaultUsername);

            RegisterChatPanelBindings();

            NetworkInterface.Instance.LocalPlayer.PlayerStatusChangedEvent += (_, status) =>
            {
                _playerStatus.Update(status.ToString());
                if (status == PlayerStatus.LOADING_MAP)
                {
                    _hubMenuVisible.Update(false);
                    _joinMenuVisible.Update(false);
                    _hostMenuVisible.Update(false);
                }
            };
            NetworkInterface.Instance.LocalPlayer.PlayerTypeChangedEvent += (_, type) =>
            {
                _playerType.Update(type.ToString());
            };

        }

        private void RefreshModSupport()
        {
            _modSupportStatus.Update(DlcCompat.GetDlcSupport().Concat(ModCompat.GetModSupport()).ToList());
        }

        private void ShowMultiplayerMenu()
        {
            RefreshModSupport();
            _joinErrorMessage.Update(new List<string>());
            Log.Debug($"UI: ShowMultiplayerMenu mode={_gameMode} status={_playerStatus.value} type={_playerType.value}");

            _hubMenuVisible.Update(true);
            _joinMenuVisible.Update(false);
            _hostMenuVisible.Update(false);

            if (_gameMode == GameMode.Game)
            {
                _activeGameScreenBinding.Update((GameScreenUISystem.GameScreen)99);
                return;
            }

            _activeMenuScreenBinding.Update(99);
        }

        private void ShowJoinGameMenu()
        {
            Log.Debug($"UI: ShowJoinGameMenu status={_playerStatus.value} type={_playerType.value}");
            _hubMenuVisible.Update(false);
            _joinMenuVisible.Update(true);
            _hostMenuVisible.Update(false);
        }

        private void ShowHostGameMenu()
        {
            Log.Debug($"UI: ShowHostGameMenu status={_playerStatus.value} type={_playerType.value}");
            _hubMenuVisible.Update(false);
            _joinMenuVisible.Update(false);
            _hostMenuVisible.Update(true);
        }

        private void HideMultiplayerHub()
        {
            Log.Debug("UI: HideMultiplayerHub");
            _hubMenuVisible.Update(false);
            _joinMenuVisible.Update(false);
            _hostMenuVisible.Update(false);

            _activeMenuScreenBinding.Update(0);
            _activeGameScreenBinding.Update(GameScreenUISystem.GameScreen.PauseMenu);
        }

        private void HideJoinGameMenu()
        {
            Log.Debug("UI: HideJoinGameMenu");
            _hubMenuVisible.Update(true);
            _joinMenuVisible.Update(false);
            _hostMenuVisible.Update(false);
        }

        private void HideHostGameMenu()
        {
            Log.Debug("UI: HideHostGameMenu");
            _hubMenuVisible.Update(true);
            _joinMenuVisible.Update(false);
            _hostMenuVisible.Update(false);
        }

        private void JoinGame()
        {
            Log.Debug($"UI: JoinGame clicked status={_playerStatus.value} type={_playerType.value}");
            NetworkInterface.Instance.UpdateLocalPlayerUsername(_username.value);
            string token = _joinToken.value?.Trim();
            if (!string.IsNullOrEmpty(token))
            {
                NetworkInterface.Instance.Connect(new ConnectionConfig(token, _joinPassword.value));
            }
            else
            {
                NetworkInterface.Instance.Connect(new ConnectionConfig(_joinIPAddress.value, _joinPort.value, _joinPassword.value));
            }
        }

        private void HostGame()
        {
            Log.Debug($"UI: HostGame clicked status={_playerStatus.value} type={_playerType.value}");
            NetworkInterface.Instance.UpdateLocalPlayerUsername(_username.value);
            NetworkInterface.Instance.StartServer(new ConnectionConfig(_hostPort.value, _hostPassword.value));
        }

        private void StopServer()
        {
            Log.Debug($"UI: StopServer clicked status={_playerStatus.value} type={_playerType.value}");
            NetworkInterface.Instance.StopServer();
        }

        private void LeaveSession()
        {
            Log.Debug($"UI: LeaveSession clicked status={_playerStatus.value} type={_playerType.value}");
            NetworkInterface.Instance.StopServer();
            _joinErrorMessage.Update(new List<string>());
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _gameMode = mode;
        }

        /// <summary>
        ///     Pumps the LiteNetLib event loop while the local player is in a
        ///     session. Without this, NetworkManager.Receive / Connect callbacks
        ///     never fire and Host / Join silently no-op.
        /// </summary>
        protected override void OnUpdate()
        {
            base.OnUpdate();
            try
            {
                NetworkInterface.Instance.OnUpdate();
            }
            catch (Exception ex)
            {
                Log.Warn($"UISystem.OnUpdate: network pump failed: {ex.Message}");
            }
        }

        private void RegisterChatPanelBindings()
        {
            AddBinding(ChatPanel.ChatMessages);
            AddBinding(ChatPanel.CurrentUsername);
            AddBinding(ChatPanel.LocalChatMessage);
            AddBinding(ChatPanel.SendChatMessage);
            AddBinding(ChatPanel.SetLocalChatMessage);
        }

        public void SetLoadProgress(int downloadDone, int downloadRemaining)
        {
            if (downloadDone == 0)
            {
                _downloadTimer.Restart();
                _lastDownloadDone = 0;
            }

            long elapsedMillis = _downloadTimer.ElapsedMilliseconds;
            if (elapsedMillis > 500)
            {
                int bytesDiff = downloadDone - _lastDownloadDone;
                _downloadTimer.Restart();
                _lastDownloadDone = downloadDone;
                _downloadSpeed.Update((int)((bytesDiff / elapsedMillis) * 1000));
            }

            _downloadDone.Update(downloadDone);
            _downloadRemaining.Update(downloadRemaining);
        }

        public void SetJoinErrors(params string[] errorMessageKey)
        {
            _joinErrorMessage.Update(errorMessageKey.ToList());
        }
    }
}

