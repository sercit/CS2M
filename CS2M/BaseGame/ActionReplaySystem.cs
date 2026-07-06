using Game;

namespace CS2M.BaseGame
{
    /// <summary>
    /// Drains <see cref="GameThreadDispatcher"/> every ECS frame.
    /// All network command handlers enqueue their replay lambdas here so that
    /// game API calls (NetToolSystem.Apply, EntityCommandBuffer, etc.) run inside
    /// the allowed ECS update window — equivalent to CSM's OnAfterSimulationTick.
    /// </summary>
    public partial class ActionReplaySystem : GameSystemBase
    {
        protected override void OnUpdate()
        {
            while (GameThreadDispatcher.TryDequeue(out var action))
            {
                try { action(); }
                catch (System.Exception ex)
                {
                    Log.Warn($"ActionReplaySystem: action threw: {ex.Message}");
                }
            }
        }
    }
}
