using CS2M.API.Commands;
using CS2M.BaseGame;
using CS2M.BaseGame.Commands;
using System.Collections.Generic;

namespace CS2M.Commands.Handler.BaseGame
{
    public class BulldozeCommandHandler : CommandHandler<BulldozeCommand>
    {
        private const int NonceHistoryLimit = 2048;

        private static readonly object RequestNonceLock = new();
        private static readonly HashSet<int> ProcessedRequestNonces = new();
        private static readonly Queue<int> ProcessedRequestNonceOrder = new();

        private static readonly object ReplicationNonceLock = new();
        private static readonly HashSet<int> ProcessedReplicationNonces = new();
        private static readonly Queue<int> ProcessedReplicationNonceOrder = new();

        public BulldozeCommandHandler()
        {
        }

        protected override void Handle(BulldozeCommand command)
        {
            if (command == null)
            {
                return;
            }

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

        private static void HandleOnServer(BulldozeCommand command)
        {
            if (!command.RequestOnly)
            {
                return;
            }

            if (!MarkBulldozeNonce(command.BulldozeNonce, ProcessedRequestNonces, ProcessedRequestNonceOrder, RequestNonceLock))
            {
                Log.Debug($"BulldozeCommandHandler: duplicate request nonce {command.BulldozeNonce} ignored.");
                return;
            }

            bool applied = BulldozeService.TryApplyBulldoze(command);
            if (!applied)
            {
                Log.Warn(
                    $"BulldozeCommandHandler: failed to apply request for {command.TargetEntityIndex}:{command.TargetEntityVersion}.");
                return;
            }

            var replication = (BulldozeCommand)command.Clone();
            replication.RequestOnly = false;
            Command.SendToClients?.Invoke(replication);

            // Log activity to the cooperative ledger
            CS2M.Systems.CooperativeSyncSystem.RegisterActivity(
                CS2M.Systems.CooperativeSyncSystem.ResolveUsername(command.SenderId),
                "Demolished objects",
                Unity.Mathematics.float3.zero
            );
        }

        private static void HandleOnClient(BulldozeCommand command)
        {
            if (command.RequestOnly)
            {
                return;
            }

            if (!MarkBulldozeNonce(command.BulldozeNonce, ProcessedReplicationNonces, ProcessedReplicationNonceOrder, ReplicationNonceLock))
            {
                Log.Debug($"BulldozeCommandHandler: duplicate replicated nonce {command.BulldozeNonce} ignored.");
                return;
            }

            CS2M.BaseGame.GameThreadDispatcher.Enqueue(() =>
            {
                bool applied = BulldozeService.TryApplyBulldoze(command);
                if (!applied) { Log.Warn($"BulldozeCommandHandler: failed to apply replicated bulldoze for {command.TargetEntityIndex}:{command.TargetEntityVersion}."); return; }

                CS2M.Systems.CooperativeSyncSystem.RegisterActivity(
                    CS2M.Systems.CooperativeSyncSystem.ResolveUsername(command.SenderId),
                    "Demolished objects", Unity.Mathematics.float3.zero);
            });
        }

        private static bool MarkBulldozeNonce(int nonce, HashSet<int> set, Queue<int> order, object sync)
        {
            if (nonce == 0)
            {
                Log.Warn($"BulldozeCommandHandler: rejected command with bulldoze nonce 0.");
                return false;
            }

            lock (sync)
            {
                bool added = set.Add(nonce);
                if (!added)
                {
                    return false;
                }

                order.Enqueue(nonce);
                while (order.Count > NonceHistoryLimit)
                {
                    int evicted = order.Dequeue();
                    set.Remove(evicted);
                }

                return true;
            }
        }
    }
}
