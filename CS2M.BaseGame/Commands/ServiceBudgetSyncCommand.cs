using CS2M.API.Commands;
using MessagePack;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     Replicates a city service budget change (percentage 0-200) to all players.
    ///     The service is identified by ECS entity index + version so it survives
    ///     network round-trips without depending on a prefab name.
    /// </summary>
    [MessagePackObject]
    public class ServiceBudgetSyncCommand : CommandBase
    {
        [Key(0)]
        public int ServiceEntityIndex { get; set; }

        [Key(1)]
        public int ServiceEntityVersion { get; set; }

        [Key(2)]
        public int Percentage { get; set; }

        public override bool Validate() => true;
    }
}
