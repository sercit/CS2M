using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using LiteNetLib;
using System.Threading;
using Unity.Entities;

namespace CS2M.Commands.Handler.BaseGame
{
    /// <summary>
    ///     Handles incoming money commands with validation and rate limiting
    /// </summary>
    public class MoneyCommandHandler : ClientCommandHandler<MoneyCommand>
    {
        // Rate limiting: max 60 updates per second
        private static readonly System.Collections.Generic.List<long> _lastUpdateTimes = new();
        private const int MAX_UPDATES_PER_SECOND = 60;

        protected override void OnValidatedCommand(MoneyCommand command)
        {
            try
            {
                Log.Debug($"Received money update: {command.Money}");

                // Check rate limit
                if (!CheckRateLimit())
                {
                    Log.Warn($"Money update rate limit exceeded, ignoring update");
                    return;
                }

                // Validate authority epoch
                long currentEpoch = GetAuthorityEpoch();
                if (command.AuthorityEpoch <= currentEpoch)
                {
                    Log.Trace($"Ignoring outdated money authority, epoch: {command.AuthorityEpoch} <= {currentEpoch}");
                    return;
                }

                // Update authority epoch
                SetAuthorityEpoch(command.AuthorityEpoch);

                // Apply money value through sync system
                var moneySystem = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<global::CS2M.BaseGame.Systems.MoneySyncSystem>();
                if (moneySystem != null)
                {
                    moneySystem.ReceiveMoneyUpdate(command);
                }
                else
                {
                    Log.Error("MoneySyncSystem not available, cannot apply money update");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"Failed to process money update: {ex.Message}", ex);
            }
        }

        private bool CheckRateLimit()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            long frequency = System.Diagnostics.Stopwatch.Frequency;
            long oneSecondTicks = frequency;

            // Remove old entries
            while (_lastUpdateTimes.Count > 0 && 
                   now - _lastUpdateTimes[0] > oneSecondTicks)
            {
                _lastUpdateTimes.RemoveAt(0);
            }

            // Check if under limit
            if (_lastUpdateTimes.Count >= MAX_UPDATES_PER_SECOND)
            {
                return false;
            }

            // Record this update
            _lastUpdateTimes.Add(now);
            return true;
        }

        private static long _currentEpoch = 0;

        private long GetAuthorityEpoch()
        {
            return Interlocked.Read(ref _currentEpoch);
        }

        private void SetAuthorityEpoch(long epoch)
        {
            Interlocked.Exchange(ref _currentEpoch, epoch);
            Log.Debug($"Updated authority epoch to {epoch}");
        }
    }
}
