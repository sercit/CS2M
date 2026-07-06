using System;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.Helpers;
using Unity.Entities;
using Game.UI.InGame;

namespace CS2M.Commands.Handler.BaseGame
{
    public class ServiceBudgetSyncCommandHandler : CommandHandler<ServiceBudgetSyncCommand>
    {
        protected override void Handle(ServiceBudgetSyncCommand command)
        {
            if (command == null)
            {
                return;
            }

            // On server: replicate to all other clients
            if (Command.CurrentRole == MultiplayerRole.Server)
            {
                Log.Info($"ServiceBudgetSyncCommandHandler: relaying to clients entity={command.ServiceEntityIndex}:{command.ServiceEntityVersion} → {command.Percentage}%.");
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

            var budgetUISystem = world.GetExistingSystemManaged<ServiceBudgetUISystem>();
            if (budgetUISystem == null)
            {
                Log.Warn("ServiceBudgetSyncCommandHandler: ServiceBudgetUISystem not found.");
                return;
            }

            Entity serviceEntity = new Entity
            {
                Index = command.ServiceEntityIndex,
                Version = command.ServiceEntityVersion
            };

            try
            {
                using (CS2M.BaseGame.ReplayScope.BeginReplayScope())
                {
                    // SetServiceBudget is private — call via reflection
                    ReflectionHelper.Call(
                        budgetUISystem,
                        "SetServiceBudget",
                        new[] { typeof(Entity), typeof(int) },
                        serviceEntity,
                        command.Percentage);
                }

                Log.Info($"ServiceBudgetSyncCommandHandler: applied budget {command.ServiceEntityIndex}:{command.ServiceEntityVersion} → {command.Percentage}%.");
            }
            catch (Exception ex)
            {
                Log.Warn($"ServiceBudgetSyncCommandHandler: failed — {ex.Message}");
            }
        }
    }
}
