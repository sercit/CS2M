using System.Collections.Generic;
using CS2M.API.Commands;
using CS2M.BaseGame;
using CS2M.BaseGame.Commands;

namespace CS2M.Commands.Handler.BaseGame
{
    public class AreaApplyCommandHandler : CommandHandler<AreaApplyCommand>
    {
        private const int NonceHistoryLimit = 4096;

        private static readonly object RequestNonceLock = new();
        private static readonly HashSet<int> ProcessedRequestNonces = new();
        private static readonly Queue<int> ProcessedRequestNonceOrder = new();

        private static readonly object ReplicationNonceLock = new();
        private static readonly HashSet<int> ProcessedReplicationNonces = new();
        private static readonly Queue<int> ProcessedReplicationNonceOrder = new();

        public AreaApplyCommandHandler()
        {
        }

        protected override void Handle(AreaApplyCommand command)
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

        private static void HandleOnServer(AreaApplyCommand command)
        {
            if (!command.RequestOnly)
            {
                return;
            }

            if (!MarkNonce(command.ApplyNonce, ProcessedRequestNonces, ProcessedRequestNonceOrder, RequestNonceLock))
            {
                Log.Debug($"AreaApplyCommandHandler: duplicate request nonce {command.ApplyNonce} ignored.");
                return;
            }

            Log.Info($"AreaApplyCommandHandler: queued client area request nonce={command.ApplyNonce}, prefab={command.PrefabName}.");
            AreaSyncService.PendingServerRequests.Enqueue(command);
        }

        internal static void ApplyPendingServerRequest(AreaApplyCommand command)
        {
            bool applied = AreaSyncService.TryReplayApply(command);
            if (!applied)
            {
                Log.Warn($"AreaApplyCommandHandler: failed to apply area request nonce {command.ApplyNonce}.");
                return;
            }

            Log.Info($"AreaApplyCommandHandler: applied client area request nonce={command.ApplyNonce}, relaying to all clients.");
            var replication = (AreaApplyCommand)command.Clone();
replication.RequestOnly = false;
            Command.SendToClients?.Invoke(replication);

            Unity.Mathematics.float3 pos = Unity.Mathematics.float3.zero;
            if (command.ControlPoints != null && command.ControlPoints.Length > 0 && command.ControlPoints[0] != null)
                pos = new Unity.Mathematics.float3(command.ControlPoints[0].PositionX, command.ControlPoints[0].PositionY, command.ControlPoints[0].PositionZ);

            string areaName = string.IsNullOrEmpty(command.PrefabName) ? "Area boundaries" : command.PrefabName;
            CS2M.Systems.CooperativeSyncSystem.RegisterActivity(
                CS2M.Systems.CooperativeSyncSystem.ResolveUsername(command.SenderId),
                $"Modified area: {areaName}", pos);
        }

        private static void HandleOnClient(AreaApplyCommand command)
        {
            if (command.RequestOnly)
            {
                return;
            }

            if (!MarkNonce(command.ApplyNonce, ProcessedReplicationNonces, ProcessedReplicationNonceOrder, ReplicationNonceLock))
            {
                Log.Debug($"AreaApplyCommandHandler: duplicate replicated nonce {command.ApplyNonce} ignored.");
                return;
            }

            // Defer to AreaToolSystem.OnUpdate via ToolReplayPatch — that is the only ECS
            // context where SafeCommandBufferSystem.CreateCommandBuffer() is allowed.
            AreaSyncService.PendingReplays.Enqueue(command);
            Log.Info($"AreaApplyCommandHandler: queued area for replay, nonce={command.ApplyNonce}");
        }

        private static bool MarkNonce(int nonce, HashSet<int> set, Queue<int> order, object sync)
        {
            if (nonce == 0)
            {
                Log.Warn($"AreaApplyCommandHandler: rejected command with apply nonce 0.");
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
