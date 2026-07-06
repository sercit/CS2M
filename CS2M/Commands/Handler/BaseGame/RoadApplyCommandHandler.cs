using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame;
using CS2M.BaseGame.Commands;

namespace CS2M.Commands.Handler.BaseGame
{
    public class RoadApplyCommandHandler : CommandHandler<RoadApplyCommand>
    {
        private const int NonceHistoryLimit = 4096;

        private static readonly object RequestNonceLock = new();
        private static readonly HashSet<int> ProcessedRequestNonces = new();
        private static readonly Queue<int> ProcessedRequestNonceOrder = new();

        private static readonly object ReplicationNonceLock = new();
        private static readonly HashSet<int> ProcessedReplicationNonces = new();
        private static readonly Queue<int> ProcessedReplicationNonceOrder = new();

        public RoadApplyCommandHandler()
        {
        }

        protected override void Handle(RoadApplyCommand command)
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

        private static void HandleOnServer(RoadApplyCommand command)
        {
            if (!command.RequestOnly)
            {
                return;
            }

            if (!MarkNonce(command.ApplyNonce, ProcessedRequestNonces, ProcessedRequestNonceOrder, RequestNonceLock))
            {
                Log.Debug($"RoadApplyCommandHandler: duplicate request nonce {command.ApplyNonce} ignored.");
                return;
            }

            bool applied = RoadSyncService.TryReplayApply(command);
            if (!applied)
            {
                Log.Warn($"RoadApplyCommandHandler: failed to apply road request nonce {command.ApplyNonce}.");
                return;
            }

            var replication = (RoadApplyCommand)command.Clone();
            replication.RequestOnly = false;
            Command.SendToClients?.Invoke(replication);

            // Log activity to the cooperative ledger
            Unity.Mathematics.float3 pos = Unity.Mathematics.float3.zero;
            if (command.LastRaycastPoint != null)
            {
                pos = new Unity.Mathematics.float3(command.LastRaycastPoint.PositionX, command.LastRaycastPoint.PositionY, command.LastRaycastPoint.PositionZ);
            }
            else if (command.ApplyStartPoint != null)
            {
                pos = new Unity.Mathematics.float3(command.ApplyStartPoint.PositionX, command.ApplyStartPoint.PositionY, command.ApplyStartPoint.PositionZ);
            }
            else if (command.ControlPoints != null && command.ControlPoints.Length > 0 && command.ControlPoints[0] != null)
            {
                pos = new Unity.Mathematics.float3(command.ControlPoints[0].PositionX, command.ControlPoints[0].PositionY, command.ControlPoints[0].PositionZ);
            }

            CS2M.Systems.CooperativeSyncSystem.RegisterActivity(
                CS2M.Systems.CooperativeSyncSystem.ResolveUsername(command.SenderId),
                $"Built road: {command.PrefabName}",
                pos
            );
        }

        private static void HandleOnClient(RoadApplyCommand command)
        {
            if (command.RequestOnly)
            {
                return;
            }

            Log.Info($"RoadApplyCommandHandler: received road from server, nonce={command.ApplyNonce}, prefab={command.PrefabName}.");

            if (!MarkNonce(command.ApplyNonce, ProcessedReplicationNonces, ProcessedReplicationNonceOrder, ReplicationNonceLock))
            {
                Log.Debug($"RoadApplyCommandHandler: duplicate replicated nonce {command.ApplyNonce} ignored.");
                return;
            }

            // Queue for replay on the next ECS OnUpdate — EntityCommandBuffer is only
            // allowed inside the ECS update phase, not in a network callback.
            RoadSyncService.PendingReplays.Enqueue(command);
            Log.Info($"RoadApplyCommandHandler: queued road nonce={command.ApplyNonce} for ECS replay.");
        }

        private static bool MarkNonce(int nonce, HashSet<int> set, Queue<int> order, object sync)
        {
            if (nonce == 0)
            {
                Log.Warn($"RoadApplyCommandHandler: rejected command with apply nonce 0.");
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
