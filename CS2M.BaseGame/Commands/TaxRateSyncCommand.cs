using CS2M.API.Commands;
using MessagePack;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     Replicates a single tax-rate setter call from one player to all others.
    ///     SetterMethod maps to the TaxSystem method that was intercepted:
    ///       0 = SetTaxRate(TaxAreaType, rate)
    ///       1 = SetResidentialTaxRate(jobLevel, rate)
    ///       2 = SetCommercialTaxRate(Resource, rate)
    ///       3 = SetIndustrialTaxRate(Resource, rate)
    ///       4 = SetOfficeTaxRate(Resource, rate)
    /// </summary>
    [MessagePackObject]
    public class TaxRateSyncCommand : CommandBase
    {
        [Key(0)]
        public int SetterMethod { get; set; }

        /// <summary>TaxAreaType (cast to int), job level, or Resource (cast to int).</summary>
        [Key(1)]
        public int Param1 { get; set; }

        [Key(2)]
        public int Rate { get; set; }

        public override bool Validate() => true;
    }
}
