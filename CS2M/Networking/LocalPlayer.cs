using System;
using System.Collections.Generic;
using System.Linq;
using Colossal;
using Colossal.PSI.Common;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands;
using CS2M.Commands.ApiServer;
using CS2M.Commands.Data.Internal;
using CS2M.Helpers;
using CS2M.Mods;
using CS2M.UI;
using CS2M.Util;
using LiteNetLib;
using Unity.Entities;

namespace CS2M.Networking
{
    public class LocalPlayer : Player
    {
        private const int MaxWorldLoadRetries = 2;

        private SlicedPacketStream _packetStream;
        private readonly SaveLoadHelper _saveLoadHelper;
        private NetworkManager _networkManager;
        private UISystem _uiSystem;
        private int _worldLoadRetryCount;
        private int _currentTransferId = -1;
        private int _lastSliceIndex = -1;

        public LocalPlayer()
        {
            PlayerStatusChangedEvent += PlayerStatusChanged;
            PlayerTypeChangedEvent += PlayerTypeChanged;
            _saveLoadHelper = EnsureSaveLoadHelper();
        }

        private SaveLoadHelper EnsureSaveLoadHelper()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            return world != null ? world.GetOrCreateSystemManaged<SaveLoadHelper>() : null;
        }

        private UISystem EnsureUiSystem()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            return world != null ? world.GetOrCreateSystemManaged<UISystem>() : null;
        }

        private void EnsureNetworkManager()
        {
            if (_networkManager == null)
            {
                _networkManager = new NetworkManager();
            }
        }

        public bool GetServerInfo(ConnectionConfig connectionConfig)
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                return false;
            }

            EnsureNetworkManager();

            _networkManager.NatHolePunchSuccessfulEvent += NatConnect;
            _networkManager.NatHolePunchFailedEvent += DirectConnect;
            _networkManager.ClientConnectSuccessfulEvent += ConnectionEstablished;
            _networkManager.ClientConnectFailedEvent += ConnectionFailed;
            _networkManager.ClientDisconnectEvent += Inactive;

            if (!_networkManager.InitConnect(connectionConfig))
            {
                _uiSystem?.SetJoinErrors("CS2M.UI.JoinError.ClientFailed");
                return false;
            }

            if (!_networkManager.SetupNatConnect())
            {
                _uiSystem?.SetJoinErrors("CS2M.UI.JoinError.InvalidIP");
                return false;
            }

            PlayerType = PlayerType.CLIENT;
            PlayerStatus = PlayerStatus.GET_SERVER_INFO;
            return true;
        }

        public bool NatConnect()
        {
            if (PlayerStatus != PlayerStatus.GET_SERVER_INFO)
            {
                return false;
            }

            if (!_networkManager.Connect())
            {
                _uiSystem?.SetJoinErrors("CS2M.UI.JoinError.FailedToConnect");
                Inactive();
                return false;
            }

            PlayerStatus = PlayerStatus.NAT_CONNECT;
            return true;
        }

        public bool DirectConnect()
        {
            if (PlayerStatus != PlayerStatus.GET_SERVER_INFO &&
                PlayerStatus != PlayerStatus.NAT_CONNECT)
            {
                return false;
            }

            if (!_networkManager.Connect())
            {
                _uiSystem?.SetJoinErrors("CS2M.UI.JoinError.FailedToConnect");
                Inactive();
                return false;
            }

            PlayerStatus = PlayerStatus.DIRECT_CONNECT;
            return true;
        }

        public bool ConnectionFailed()
        {
            _uiSystem?.SetJoinErrors("CS2M.UI.JoinError.FailedToConnect");
            Inactive();
            return true;
        }

        public bool ConnectionEstablished()
        {
            if (PlayerStatus != PlayerStatus.NAT_CONNECT &&
                PlayerStatus != PlayerStatus.DIRECT_CONNECT)
            {
                return false;
            }

            SendToServer(new PreconditionsCheckCommand
            {
                Username = Username,
                Password = GetConnectionPassword(),
                ModVersion = VersionUtil.GetModVersion(),
                GameVersion = VersionUtil.GetGameVersion(),
                Mods = ModSupport.Instance.RequiredModsForSync,
                DlcIds = DlcCompat.RequiredDLCsForSync,
            });

            _worldLoadRetryCount = 0;
            _currentTransferId = -1;
            _lastSliceIndex = -1;
            PlayerStatus = PlayerStatus.CONNECTION_ESTABLISHED;
            return true;
        }

        public void PreconditionsError(PreconditionsErrorCommand command)
        {
            Inactive();
            var errors = new List<string>();
            PreconditionsUtil.Errors err = command.Errors;
            if (err.HasFlag(PreconditionsUtil.Errors.GAME_VERSION_MISMATCH))
            {
                errors.Add("precondition:GAME_VERSION_MISMATCH");
                errors.Add(command.GameVersion.ToString());
                errors.Add(VersionUtil.GetGameVersion().ToString());
            }

            if (err.HasFlag(PreconditionsUtil.Errors.MOD_VERSION_MISMATCH))
            {
                errors.Add("precondition:MOD_VERSION_MISMATCH");
                errors.Add(command.ModVersion.ToString());
                errors.Add(VersionUtil.GetModVersion().ToString());
            }

            if (err.HasFlag(PreconditionsUtil.Errors.USERNAME_NOT_AVAILABLE))
            {
                errors.Add("precondition:USERNAME_NOT_AVAILABLE");
            }

            if (err.HasFlag(PreconditionsUtil.Errors.PASSWORD_INCORRECT))
            {
                errors.Add("precondition:PASSWORD_INCORRECT");
            }

            if (err.HasFlag(PreconditionsUtil.Errors.DLCS_MISMATCH))
            {
                List<int> clientDLCs = DlcCompat.RequiredDLCsForSync;
                List<int> serverDLCs = command.DlcIds;

                IEnumerable<string> clientNotServer = clientDLCs.Where(mod => !serverDLCs.Contains(mod))
                    .Select(id => DlcCompat.GetDisplayName(new DlcId(id)));
                IEnumerable<string> serverNotClient = serverDLCs.Where(mod => !clientDLCs.Contains(mod))
                    .Select(id => DlcCompat.GetDisplayName(new DlcId(id)));

                errors.Add("precondition:DLCS_MISMATCH");
                errors.Add(string.Join(", ", serverNotClient));
                errors.Add(string.Join(", ", clientNotServer));
            }

            if (err.HasFlag(PreconditionsUtil.Errors.MODS_MISMATCH))
            {
                List<string> clientMods = ModSupport.Instance.RequiredModsForSync;
                List<string> serverMods = command.Mods;

                IEnumerable<string> clientNotServer = clientMods.Where(mod => !serverMods.Contains(mod));
                IEnumerable<string> serverNotClient = serverMods.Where(mod => !clientMods.Contains(mod));

                errors.Add("precondition:MODS_MISMATCH");
                errors.Add(string.Join(", ", serverNotClient));
                errors.Add(string.Join(", ", clientNotServer));
            }

            _uiSystem?.SetJoinErrors(errors.ToArray());
        }

        public bool WaitingToJoin(bool isRetry = false)
        {
            bool validTransition = PlayerStatus == PlayerStatus.CONNECTION_ESTABLISHED ||
                                   (isRetry && PlayerStatus == PlayerStatus.LOADING_MAP);
            if (!validTransition)
            {
                return false;
            }

            if (!isRetry)
            {
                _worldLoadRetryCount = 0;
            }

            _packetStream = null;
            _currentTransferId = -1;
            _lastSliceIndex = -1;
            PlayerStatus = PlayerStatus.WAITING_TO_JOIN;
            _uiSystem?.SetLoadProgress(0, 0);
            SendToServer(new JoinRequestCommand
            {
                Username = Username,
                ModVersion = VersionUtil.GetModVersion().ToString(),
                GameVersion = VersionUtil.GetGameVersionString(),
                ConnectionTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            Log.Debug(isRetry
                ? "LocalPlayer: world transfer retry request sent to server."
                : "LocalPlayer: join request sent to server.");
            return true;
        }

        public bool DownloadingMap()
        {
            if (PlayerStatus != PlayerStatus.WAITING_TO_JOIN &&
                PlayerStatus != PlayerStatus.DOWNLOADING_MAP)
            {
                return false;
            }

            PlayerStatus = PlayerStatus.DOWNLOADING_MAP;
            _packetStream = null;
            _uiSystem?.SetLoadProgress(0, 0);
            return true;
        }

        public void SliceReceived(WorldTransferCommand cmd)
        {
            if (cmd == null || cmd.WorldSlice == null || cmd.WorldSlice.Length == 0)
            {
                Log.Warn("Received invalid world slice payload.");
                return;
            }

            if (PlayerStatus == PlayerStatus.WAITING_TO_JOIN)
            {
                if (!DownloadingMap())
                {
                    return;
                }
            }

            if (PlayerStatus != PlayerStatus.DOWNLOADING_MAP)
            {
                Log.Warn("Received world slice, but not in downloading state");
                return;
            }

            if (cmd.NewTransfer)
            {
                if (cmd.TransferId <= _currentTransferId)
                {
                    Log.Debug(
                        $"Ignoring stale world transfer start. TransferId={cmd.TransferId}, CurrentTransferId={_currentTransferId}");
                    return;
                }

                _currentTransferId = cmd.TransferId;
                _lastSliceIndex = -1;
                _packetStream = new SlicedPacketStream(cmd.WorldSlice.Length);
            }
            else
            {
                if (cmd.TransferId != _currentTransferId)
                {
                    Log.Debug(
                        $"Ignoring stale world slice for transfer {cmd.TransferId}. Current transfer is {_currentTransferId}.");
                    return;
                }

                if (_packetStream == null)
                {
                    Log.Warn("Received world slice without initialized packet stream");
                    _uiSystem?.SetJoinErrors("CS2M.UI.JoinError.DownloadFailed");
                    Inactive();
                    return;
                }
            }

            if (cmd.SliceIndex != _lastSliceIndex + 1)
            {
                Log.Warn(
                    $"World transfer slice sequence mismatch. Expected {_lastSliceIndex + 1}, got {cmd.SliceIndex}.");
                _uiSystem?.SetJoinErrors("CS2M.UI.JoinError.DownloadFailed");
                Inactive();
                return;
            }

            if (!_packetStream.AppendSlice(cmd.WorldSlice))
            {
                Log.Warn("Failed to append world slice to packet stream.");
                _uiSystem?.SetJoinErrors("CS2M.UI.JoinError.DownloadFailed");
                Inactive();
                return;
            }

            if (cmd.RemainingBytes < 0)
            {
                Log.Warn($"Received invalid remaining byte count {cmd.RemainingBytes} for world transfer.");
                _uiSystem?.SetJoinErrors("CS2M.UI.JoinError.DownloadFailed");
                Inactive();
                return;
            }

            _lastSliceIndex = cmd.SliceIndex;
            _uiSystem?.SetLoadProgress((int)_packetStream.Length, cmd.RemainingBytes);

            if (cmd.RemainingBytes == 0)
            {
                LoadingMap();
            }
        }

        public void LoadingMap()
        {
            if (PlayerStatus != PlayerStatus.DOWNLOADING_MAP)
            {
                return;
            }

            PlayerStatus = PlayerStatus.LOADING_MAP;
            TaskManager.instance.EnqueueTask("LoadMap", async () =>
            {
                bool success = false;
                try
                {
                    success = await _saveLoadHelper.LoadGame(_packetStream);
                }
                catch (Exception ex)
                {
                    Log.Warn($"LoadGame failed with exception: {ex}");
                }

                if (success)
                {
                    _packetStream = null; // Clean up save game memory
                    Playing();
                    return;
                }

                _packetStream = null;
                if (!RetryWorldLoad("save load failed"))
                {
                    _uiSystem?.SetJoinErrors("CS2M.UI.JoinError.DownloadFailed");
                    Inactive();
                }
            });
        }

        private bool RetryWorldLoad(string reason)
        {
            if (PlayerType != PlayerType.CLIENT)
            {
                return false;
            }

            if (_worldLoadRetryCount >= MaxWorldLoadRetries)
            {
                Log.Warn($"World load retry limit reached. Last failure reason: {reason}.");
                return false;
            }

            _worldLoadRetryCount++;
            Log.Warn(
                $"Retrying world transfer ({_worldLoadRetryCount}/{MaxWorldLoadRetries}) after failure: {reason}.");
            return WaitingToJoin(true);
        }

        public bool Playing()
        {
            if (PlayerStatus != PlayerStatus.LOADING_MAP)
            {
                return false;
            }

            _worldLoadRetryCount = 0;
            _currentTransferId = -1;
            _lastSliceIndex = -1;
            PlayerStatus = PlayerStatus.PLAYING;

            if (PlayerType == PlayerType.CLIENT)
            {
                SendToServer(new JoinReadyCommand());
            }

            return true;
        }

        // INACTIVE -> PLAYING (Server)
        public bool Playing(ConnectionConfig connectionConfig)
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                return false;
            }

            EnsureNetworkManager();

            bool serverStarted = _networkManager.StartServer(connectionConfig);
            if (!serverStarted)
            {
                return false;
            }

            //TODO: Setup server variables (player list, etc.)

            PlayerStatus = PlayerStatus.PLAYING;
            PlayerType = PlayerType.SERVER;

            return true;
        }

        public void Blocked()
        {
        }

        // PLAYING -> INACTIVE
        public bool Inactive()
        {
            _networkManager?.Stop();
            _networkManager = null;
            _packetStream = null;
            _worldLoadRetryCount = 0;
            _currentTransferId = -1;
            _lastSliceIndex = -1;

            NetworkInterface.Instance.ResetRemotePlayers();

            _uiSystem?.SetLoadProgress(0, 0);
            _uiSystem?.SetJoinErrors();

            PlayerStatus = PlayerStatus.INACTIVE;
            PlayerType = PlayerType.NONE;
            return true;
        }

        public void OnUpdate()
        {
            if (_uiSystem == null)
            {
                _uiSystem = EnsureUiSystem();
            }

            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                _networkManager.ProcessEvents();
            }
        }

        public void UpdateUsername(string username)
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                //TODO: Print Warning
                return;
            }

            Username = username;
        }

        public void UpdatePlayerType(PlayerType playerType)
        {
            PlayerType = playerType;
        }

        public string GetConnectionPassword()
        {
            return _networkManager.GetConnectionPassword();
        }

        public void SendToAll(CommandBase message)
        {
            message.SenderId = PlayerId;
            if (PlayerType == PlayerType.SERVER)
            {
                _networkManager.SendToAllClients(message);
            }
            else
            {
                _networkManager.SendToServer(message);
            }
        }

        public void SendToClient(NetPeer peer, CommandBase message)
        {
            message.SenderId = PlayerId;
            _networkManager.SendToClient(peer, message);
        }

        public void SendToServer(CommandBase message)
        {
            if (PlayerType == PlayerType.CLIENT)
            {
                message.SenderId = PlayerId;
                _networkManager.SendToServer(message);
            }
        }

        public void SendToClients(CommandBase message)
        {
            if (PlayerType == PlayerType.SERVER)
            {
                message.SenderId = PlayerId;
                _networkManager.SendToAllClients(message);
            }
        }

        public void SendToApiServer(ApiCommandBase message)
        {
            _networkManager.SendToApiServer(message);
        }

        public void PlayerStatusChanged(PlayerStatus oldPlayerStatus, PlayerStatus newPlayerStatus)
        {
            Log.Debug($"LocalPlayer: changed player status from {oldPlayerStatus} to {newPlayerStatus}");
        }

        public void PlayerTypeChanged(PlayerType oldPlayerType, PlayerType newPlayerType)
        {
            Log.Debug($"LocalPlayer: changed player type from {oldPlayerType} to {newPlayerType}");
            Command.CurrentRole = newPlayerType switch
            {
                PlayerType.CLIENT => MultiplayerRole.Client,
                PlayerType.SERVER => MultiplayerRole.Server,
                _ => MultiplayerRole.None
            };
        }
    }
}
