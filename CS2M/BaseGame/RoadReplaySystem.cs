using CS2M.API.Commands;
using Game;

namespace CS2M.BaseGame
{
    /// <summary>
    /// Drains <see cref="RoadSyncService.PendingReplays"/> every ECS frame so that
    /// <see cref="RoadSyncService.TryReplayApply"/> runs inside the allowed ECS update
    /// window (EntityCommandBuffer creation is permitted here).
    /// </summary>
    public partial class RoadReplaySystem : GameSystemBase
    {
        protected override void OnUpdate()
        {
            if (Command.CurrentRole != MultiplayerRole.Client)
                return;

            while (RoadSyncService.PendingReplays.TryDequeue(out var command))
            {
                bool ok = RoadSyncService.TryReplayApply(command);
                if (!ok)
                    Log.Warn($"RoadReplaySystem: TryReplayApply failed for nonce {command.ApplyNonce}.");
                else
                    Log.Info($"RoadReplaySystem: applied road nonce {command.ApplyNonce} ({command.PrefabName}).");
            }
        }
    }
}
