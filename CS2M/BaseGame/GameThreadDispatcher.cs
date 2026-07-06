using System;
using System.Collections.Concurrent;

namespace CS2M.BaseGame
{
    /// <summary>
    /// Thread-safe queue of actions to be executed on the ECS game thread during the
    /// next <see cref="ActionReplaySystem.OnUpdate"/>. Equivalent to CSM's
    /// TransactionHandler — network callbacks enqueue here; the ECS system drains it.
    /// </summary>
    public static class GameThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> _queue = new();

        public static void Enqueue(Action action) => _queue.Enqueue(action);

        public static bool TryDequeue(out Action action) => _queue.TryDequeue(out action);
    }
}
