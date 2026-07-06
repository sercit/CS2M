using CS2M.API.Commands;
using CS2M.BaseGame;
using CS2M.BaseGame.Commands;
using System.Collections.Generic;

namespace CS2M.Commands.Handler.BaseGame
{
    public class BuildingCreateCommandHandler : CommandHandler<BuildingCreateCommand>
    {
        private const int NonceHistoryLimit = 2048;

        private static readonly object RequestNonceLock = new();
        private static readonly HashSet<int> ProcessedRequestNonces = new();
        private static readonly Queue<int> ProcessedRequestNonceOrder = new();

        private static readonly object ReplicationNonceLock = new();
        private static readonly HashSet<int> ProcessedReplicationNonces = new();
        private static readonly Queue<int> ProcessedReplicationNonceOrder = new();

        public BuildingCreateCommandHandler()
        {
        }

        protected override void Handle(BuildingCreateCommand command)
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

        private static void HandleOnServer(BuildingCreateCommand command)
        {
            if (!command.RequestOnly)
            {
                return;
            }

            if (!MarkPlacementNonce(command.PlacementNonce, ProcessedRequestNonces, ProcessedRequestNonceOrder, RequestNonceLock))
            {
                Log.Debug($"BuildingCreateCommandHandler: duplicate placement request nonce {command.PlacementNonce} ignored.");
                return;
            }

            bool applied = BuildingPlacementService.TryApplyPlacement(command);
            if (!applied)
            {
                Log.Warn($"BuildingCreateCommandHandler: failed to apply request for '{command.PrefabName}'.");
                return;
            }

            var replication = (BuildingCreateCommand)command.Clone();
            replication.RequestOnly = false;
            Command.SendToClients?.Invoke(replication);

            // Log activity to the cooperative ledger
            CS2M.Systems.CooperativeSyncSystem.RegisterActivity(
                CS2M.Systems.CooperativeSyncSystem.ResolveUsername(command.SenderId),
                $"Placed building: {command.PrefabName}",
                new Unity.Mathematics.float3(command.PositionX, command.PositionY, command.PositionZ)
            );
        }

        private static void HandleOnClient(BuildingCreateCommand command)
        {
            if (command.RequestOnly)
            {
                return;
            }

            if (!MarkPlacementNonce(command.PlacementNonce, ProcessedReplicationNonces, ProcessedReplicationNonceOrder, ReplicationNonceLock))
            {
                Log.Debug($"BuildingCreateCommandHandler: duplicate replicated placement nonce {command.PlacementNonce} ignored.");
                return;
            }

            CS2M.BaseGame.GameThreadDispatcher.Enqueue(() =>
            {
                bool applied = BuildingPlacementService.TryApplyPlacement(command);
                if (!applied) { Log.Warn($"BuildingCreateCommandHandler: failed to apply replicated placement for '{command.PrefabName}'."); return; }

                CS2M.Systems.CooperativeSyncSystem.RegisterActivity(
                    CS2M.Systems.CooperativeSyncSystem.ResolveUsername(command.SenderId),
                    $"Placed building: {command.PrefabName}",
                    new Unity.Mathematics.float3(command.PositionX, command.PositionY, command.PositionZ));
            });
        }

        private static bool MarkPlacementNonce(
            int nonce,
            HashSet<int> set,
            Queue<int> order,
            object sync)
        {
            if (nonce == 0)
            {
                Log.Warn($"BuildingCreateCommandHandler: rejected command with placement nonce 0.");
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
