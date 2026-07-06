using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame;
using CS2M.BaseGame.Commands;

namespace CS2M.Commands.Handler.BaseGame
{
    public class ZoneApplyCommandHandler : CommandHandler<ZoneApplyCommand>
    {
        private const int NonceHistoryLimit = 4096;

        private static readonly object RequestNonceLock = new();
        private static readonly HashSet<int> ProcessedRequestNonces = new();
        private static readonly Queue<int> ProcessedRequestNonceOrder = new();

        private static readonly object ReplicationNonceLock = new();
        private static readonly HashSet<int> ProcessedReplicationNonces = new();
        private static readonly Queue<int> ProcessedReplicationNonceOrder = new();

        public ZoneApplyCommandHandler()
        {
        }

        protected override void Handle(ZoneApplyCommand command)
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

        private static void HandleOnServer(ZoneApplyCommand command)
        {
            if (!command.RequestOnly)
            {
                return;
            }

            if (!MarkNonce(command.ApplyNonce, ProcessedRequestNonces, ProcessedRequestNonceOrder, RequestNonceLock))
            {
                Log.Debug($"ZoneApplyCommandHandler: duplicate request nonce {command.ApplyNonce} ignored.");
                return;
            }

            bool applied = ZoneSyncService.TryReplayApply(command);
            if (!applied)
            {
                Log.Warn($"ZoneApplyCommandHandler: failed to apply zoning request nonce {command.ApplyNonce}.");
                return;
            }

            var replication = (ZoneApplyCommand)command.Clone();
            replication.RequestOnly = false;
            Command.SendToClients?.Invoke(replication);

            // Log activity to the cooperative ledger
            Unity.Mathematics.float3 pos = Unity.Mathematics.float3.zero;
            if (command.RaycastPoint != null)
            {
                pos = new Unity.Mathematics.float3(command.RaycastPoint.PositionX, command.RaycastPoint.PositionY, command.RaycastPoint.PositionZ);
            }
            else if (command.StartPoint != null)
            {
                pos = new Unity.Mathematics.float3(command.StartPoint.PositionX, command.StartPoint.PositionY, command.StartPoint.PositionZ);
            }
            else if (command.SnapPoint != null)
            {
                pos = new Unity.Mathematics.float3(command.SnapPoint.PositionX, command.SnapPoint.PositionY, command.SnapPoint.PositionZ);
            }

            string zoneName = string.IsNullOrEmpty(command.PrefabName) ? "Zone grid" : command.PrefabName;
            CS2M.Systems.CooperativeSyncSystem.RegisterActivity(
                CS2M.Systems.CooperativeSyncSystem.ResolveUsername(command.SenderId),
                $"Applied zoning: {zoneName}",
                pos
            );
        }

        private static void HandleOnClient(ZoneApplyCommand command)
        {
            if (command.RequestOnly)
            {
                return;
            }

            if (!MarkNonce(command.ApplyNonce, ProcessedReplicationNonces, ProcessedReplicationNonceOrder, ReplicationNonceLock))
            {
                Log.Debug($"ZoneApplyCommandHandler: duplicate replicated nonce {command.ApplyNonce} ignored.");
                return;
            }

            CS2M.BaseGame.GameThreadDispatcher.Enqueue(() =>
            {
                bool applied = ZoneSyncService.TryReplayApply(command);
                if (!applied) { Log.Warn($"ZoneApplyCommandHandler: failed to apply zoning nonce {command.ApplyNonce}."); return; }

                Unity.Mathematics.float3 pos = Unity.Mathematics.float3.zero;
                if (command.RaycastPoint != null)
                    pos = new Unity.Mathematics.float3(command.RaycastPoint.PositionX, command.RaycastPoint.PositionY, command.RaycastPoint.PositionZ);
                else if (command.StartPoint != null)
                    pos = new Unity.Mathematics.float3(command.StartPoint.PositionX, command.StartPoint.PositionY, command.StartPoint.PositionZ);
                else if (command.SnapPoint != null)
                    pos = new Unity.Mathematics.float3(command.SnapPoint.PositionX, command.SnapPoint.PositionY, command.SnapPoint.PositionZ);

                string zoneName = string.IsNullOrEmpty(command.PrefabName) ? "Zone grid" : command.PrefabName;
                CS2M.Systems.CooperativeSyncSystem.RegisterActivity(
                    CS2M.Systems.CooperativeSyncSystem.ResolveUsername(command.SenderId),
                    $"Applied zoning: {zoneName}", pos);
            });
        }

        private static bool MarkNonce(int nonce, HashSet<int> set, Queue<int> order, object sync)
        {
            if (nonce == 0)
            {
                Log.Warn($"ZoneApplyCommandHandler: rejected command with apply nonce 0.");
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
