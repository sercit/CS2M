using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CS2M.BaseGame.Commands;
using CS2M.Helpers;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2M.BaseGame
{
    internal static class BuildingPlacementService
    {
        private static readonly Dictionary<string, Entity> PrefabEntityCache = new(StringComparer.Ordinal);
        private static int _placementNonceCounter;

        public static bool TryApplyPlacement(BuildingCreateCommand command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.PrefabName))
            {
                return false;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return false;
            }

            PrefabSystem prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
            if (prefabSystem == null)
            {
                Log.Warn("BuildingPlacement: PrefabSystem not available.");
                return false;
            }

            if (!TryResolvePrefabEntity(prefabSystem, command.PrefabName, out Entity prefabEntity))
            {
                Log.Warn($"BuildingPlacement: Could not resolve prefab '{command.PrefabName}'.");
                return false;
            }

            EntityManager entityManager = world.EntityManager;
            Entity definitionEntity = entityManager.CreateEntity();

            float3 position = new(command.PositionX, command.PositionY, command.PositionZ);
            quaternion rotation = new(command.RotationX, command.RotationY, command.RotationZ, command.RotationW);

            entityManager.AddComponentData(definitionEntity, new CreationDefinition
            {
                m_Prefab = prefabEntity,
                m_SubPrefab = Entity.Null,
                m_Original = Entity.Null,
                m_Owner = Entity.Null,
                m_Attached = Entity.Null,
                m_Flags = CreationFlags.Permanent,
                m_RandomSeed = command.RandomSeed
            });

            entityManager.AddComponentData(definitionEntity, new OwnerDefinition
            {
                m_Prefab = prefabEntity,
                m_Position = position,
                m_Rotation = rotation
            });

            entityManager.AddComponentData(definitionEntity, new ObjectDefinition
            {
                m_Position = position,
                m_LocalPosition = float3.zero,
                m_Scale = new float3(1f, 1f, 1f),
                m_Rotation = rotation,
                m_LocalRotation = quaternion.identity,
                m_Elevation = 0f,
                m_Intensity = 1f,
                m_Age = 0f,
                m_ParentMesh = -1,
                m_GroupIndex = 0,
                m_Probability = 100,
                m_PrefabSubIndex = 0
            });

            entityManager.AddComponentData(definitionEntity, new PrefabRef
            {
                m_Prefab = prefabEntity
            });

            entityManager.AddComponentData(definitionEntity, new Temp
            {
                m_Original = Entity.Null,
                m_CurvePosition = 0f,
                m_Value = 0,
                m_Cost = 0,
                m_Flags = TempFlags.Create
            });

            Log.Debug($"BuildingPlacement: queued placement for prefab '{command.PrefabName}'.");
            return true;
        }

        public static bool TryBuildPlacementCommand(ObjectToolSystem tool, bool requestOnly, out BuildingCreateCommand command)
        {
            command = null;
            if (tool?.prefab is not BuildingPrefab prefab)
            {
                return false;
            }

            ControlPoint lastRaycast = default;
            object lrObj = ReflectionHelper.GetAttr(tool, "m_LastRaycastPoint");
            if (lrObj is ControlPoint lrCp) lastRaycast = lrCp;
            if (lastRaycast.m_Rotation.Equals(default(quaternion)))
            {
                lastRaycast.m_Rotation = quaternion.identity;
            }

            command = new BuildingCreateCommand
            {
                PrefabName = prefab.name,
                PositionX = lastRaycast.m_Position.x,
                PositionY = lastRaycast.m_Position.y,
                PositionZ = lastRaycast.m_Position.z,
                RotationX = lastRaycast.m_Rotation.value.x,
                RotationY = lastRaycast.m_Rotation.value.y,
                RotationZ = lastRaycast.m_Rotation.value.z,
                RotationW = lastRaycast.m_Rotation.value.w,
                RandomSeed = Environment.TickCount,
                PlacementNonce = NextPlacementNonce(),
                RequestOnly = requestOnly
            };
            return true;
        }

        private static int NextPlacementNonce()
        {
            int next = Interlocked.Increment(ref _placementNonceCounter);
            if (next != 0)
            {
                return next;
            }

            return Interlocked.Increment(ref _placementNonceCounter);
        }

        private static bool TryResolvePrefabEntity(PrefabSystem prefabSystem, string prefabName, out Entity prefabEntity)
        {
            if (PrefabEntityCache.TryGetValue(prefabName, out prefabEntity) && prefabEntity != Entity.Null)
            {
                return true;
            }

            IEnumerable<PrefabBase> prefabs = ReflectionHelper.GetProp<IEnumerable<PrefabBase>>(prefabSystem, "prefabs");
            if (prefabs == null)
            {
                Log.Warn("BuildingPlacement: PrefabSystem did not expose prefab collection.");
                prefabEntity = Entity.Null;
                return false;
            }

            BuildingPrefab prefab = prefabs?
                .OfType<BuildingPrefab>()
                .FirstOrDefault(p => string.Equals(p.name, prefabName, StringComparison.Ordinal));

            if (prefab == null || !prefabSystem.TryGetEntity(prefab, out prefabEntity))
            {
                prefabEntity = Entity.Null;
                return false;
            }

            PrefabEntityCache[prefabName] = prefabEntity;
            return true;
        }
    }
}
