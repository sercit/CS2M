using CS2M.API.Commands;
using CS2M.BaseGame;
using CS2M.BaseGame.Commands;
using Unity.Entities;
using Game.Simulation;

namespace CS2M.Commands.Handler.BaseGame
{
    public class TaxRateSyncCommandHandler : CommandHandler<TaxRateSyncCommand>
    {
        protected override void Handle(TaxRateSyncCommand command)
        {
            if (command == null)
            {
                return;
            }

            // On server: replicate to all other clients (sender already applied locally)
            if (Command.CurrentRole == MultiplayerRole.Server)
            {
                Command.SendToClients?.Invoke(command);
                return;
            }

            if (Command.CurrentRole != MultiplayerRole.Client)
            {
                return;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return;
            }

            var taxSystem = world.GetExistingSystemManaged<TaxSystem>();
            if (taxSystem == null)
            {
                Log.Warn("TaxRateSyncCommandHandler: TaxSystem not found.");
                return;
            }

            using (ReplayScope.BeginReplayScope())
            {
                switch (command.SetterMethod)
                {
                    case 0: // SetTaxRate(TaxAreaType, int)
                        taxSystem.SetTaxRate((TaxAreaType)command.Param1, command.Rate);
                        break;
                    case 1: // SetResidentialTaxRate(int jobLevel, int)
                        taxSystem.SetResidentialTaxRate(command.Param1, command.Rate);
                        break;
                    case 2: // SetCommercialTaxRate(Resource, int)
                        taxSystem.SetCommercialTaxRate((Game.Economy.Resource)command.Param1, command.Rate);
                        break;
                    case 3: // SetIndustrialTaxRate(Resource, int)
                        taxSystem.SetIndustrialTaxRate((Game.Economy.Resource)command.Param1, command.Rate);
                        break;
                    case 4: // SetOfficeTaxRate(Resource, int)
                        taxSystem.SetOfficeTaxRate((Game.Economy.Resource)command.Param1, command.Rate);
                        break;
                    default:
                        Log.Warn($"TaxRateSyncCommandHandler: unknown setter method {command.SetterMethod}.");
                        break;
                }
            }

            Log.Debug($"TaxRateSyncCommandHandler: applied setter={command.SetterMethod}, param={command.Param1}, rate={command.Rate}.");
        }
    }
}
