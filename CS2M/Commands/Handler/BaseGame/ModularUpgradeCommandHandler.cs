using CS2M.API.Commands;
using CS2M.BaseGame;
using CS2M.BaseGame.Commands;
using System.Collections.Generic;

namespace CS2M.Commands.Handler.BaseGame
{
    public class ModularUpgradeCommandHandler : CommandHandler<ModularUpgradeCommand>
    {
        private const int NonceHistoryLimit = 2048;

        private static readonly object RequestNonceLock = new();
        private static readonly HashSet<int> ProcessedRequestNonces = new();
        private static readonly Queue<int> ProcessedRequestNonceOrder = new();

        private static readonly object ReplicationNonceLock = new();
        private static readonly HashSet<int> ProcessedReplicationNonces = new();
        private static readonly Queue<int> ProcessedReplicationNonceOrder = new();

        public ModularUpgradeCommandHandler()
        {
        }

        protected override void Handle(ModularUpgradeCommand command)
        {
            if (command == null) return;

            switch (Command.CurrentRole)
            {
                case MultiplayerRole.Server:
                    HandleOnServer(command);
                    break;
                case MultiplayerRole.Client:
                    HandleOnClient(command);
                    break;
            }
        }

        private static void HandleOnServer(ModularUpgradeCommand command)
        {
            if (!command.RequestOnly) return;

            if (!MarkNonce(command.UpgradeNonce, ProcessedRequestNonces, ProcessedRequestNonceOrder, RequestNonceLock))
            {
                Log.Debug($"ModularUpgradeCommandHandler: duplicate request nonce {command.UpgradeNonce} ignored.");
                return;
            }

            bool applied = ModularUpgradeService.TryApplyUpgrade(command);
            if (!applied)
            {
                Log.Warn($"ModularUpgradeCommandHandler: failed to apply upgrade '{command.UpgradePrefabName}' nonce={command.UpgradeNonce}.");
                return;
            }

            Log.Info($"ModularUpgradeCommandHandler: applied upgrade '{command.UpgradePrefabName}' nonce={command.UpgradeNonce}, relaying to clients.");
            command.RequestOnly = false;
            Command.SendToClients?.Invoke(command);

            // Log activity to cooperative registry
            CS2M.API.CooperativeActivityRegistry.RegisterActivity(
                ResolveUsername(command.SenderId),
                $"Attached building upgrade: {command.UpgradePrefabName}",
                command.PositionX, command.PositionY, command.PositionZ
            );
        }

        private static void HandleOnClient(ModularUpgradeCommand command)
        {
            if (command.RequestOnly) return;

            if (!MarkNonce(command.UpgradeNonce, ProcessedReplicationNonces, ProcessedReplicationNonceOrder, ReplicationNonceLock))
            {
                Log.Debug($"ModularUpgradeCommandHandler: duplicate replication nonce {command.UpgradeNonce} ignored.");
                return;
            }

            Log.Info($"ModularUpgradeCommandHandler: received upgrade '{command.UpgradePrefabName}' nonce={command.UpgradeNonce}, applying.");
            bool applied = ModularUpgradeService.TryApplyUpgrade(command);
            if (!applied)
            {
                Log.Warn($"ModularUpgradeCommandHandler: failed to apply upgrade '{command.UpgradePrefabName}' nonce={command.UpgradeNonce}.");
            }
            if (applied)
            {
                // Log activity to cooperative registry
                CS2M.API.CooperativeActivityRegistry.RegisterActivity(
                    ResolveUsername(command.SenderId),
                    $"Attached building upgrade: {command.UpgradePrefabName}",
                    command.PositionX, command.PositionY, command.PositionZ
                );
            }
        }

        private static bool MarkNonce(int nonce, HashSet<int> set, Queue<int> order, object sync)
        {
            if (nonce == 0) return true;
            lock (sync)
            {
                if (!set.Add(nonce)) return false;
                order.Enqueue(nonce);
                while (order.Count > NonceHistoryLimit)
                {
                    set.Remove(order.Dequeue());
                }
                return true;
            }
        }

        private static string ResolveUsername(int senderId)
        {
            if (senderId == -1) return "Server";
            if (CS2M.Networking.NetworkInterface.Instance == null || CS2M.Networking.NetworkInterface.Instance.PlayerListJoined == null)
            {
                return $"Player {senderId}";
            }
            try
            {
                for (int i = 0; i < CS2M.Networking.NetworkInterface.Instance.PlayerListJoined.Count; i++)
                {
                    var p = CS2M.Networking.NetworkInterface.Instance.PlayerListJoined[i];
                    if (p != null && p.PlayerId == senderId)
                    {
                        return p.Username ?? $"Player {senderId}";
                    }
                }
            }
            catch {}
            return $"Player {senderId}";
        }
    }
}
