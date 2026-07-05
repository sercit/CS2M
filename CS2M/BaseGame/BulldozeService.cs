using System.Threading;
using CS2M.BaseGame.Commands;
using CS2M.Helpers;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;

namespace CS2M.BaseGame
{
    internal static class BulldozeService
    {
        private static int _bulldozeNonceCounter;

        public static bool TryBuildBulldozeCommand(BulldozeToolSystem tool, bool requestOnly, out BulldozeCommand command)
        {
            command = null;
            if (tool == null)
            {
                return false;
            }

            bool isMultiSelection = ReflectionHelper.Call<bool>(tool, "IsMultiSelection");
            if (isMultiSelection)
            {
                object listObj = ReflectionHelper.GetAttr(tool, "m_ControlPoints");
                if (listObj != null)
                {
                    int length = ReflectionHelper.Call<int>(listObj, "get_Length");
                    if (length > 0)
                    {
                        var indices = new System.Collections.Generic.List<int>();
                        var versions = new System.Collections.Generic.List<int>();
                        for (int i = 0; i < length; i++)
                        {
                            object pointObj = ReflectionHelper.Call(
                                listObj,
                                "get_Item",
                                new[] { typeof(int) },
                                i);
                            if (pointObj is ControlPoint point && point.m_OriginalEntity != Entity.Null)
                            {
                                if (!indices.Contains(point.m_OriginalEntity.Index))
                                {
                                    indices.Add(point.m_OriginalEntity.Index);
                                    versions.Add(point.m_OriginalEntity.Version);
                                }
                            }
                        }

                        if (indices.Count > 0)
                        {
                            command = new BulldozeCommand
                            {
                                IsMultiSelect = true,
                                MultiTargetIndices = indices,
                                MultiTargetVersions = versions,
                                BulldozeNonce = NextBulldozeNonce(),
                                RequestOnly = requestOnly
                            };
                            return true;
                        }
                    }
                }
            }

            ControlPoint lastRaycast = default;
            object lrObj2 = ReflectionHelper.GetAttr(tool, "m_LastRaycastPoint");
            if (lrObj2 is ControlPoint lrCp2) lastRaycast = lrCp2;
            Entity target = lastRaycast.m_OriginalEntity;
            if (target == Entity.Null)
            {
                return false;
            }

            command = new BulldozeCommand
            {
                TargetEntityIndex = target.Index,
                TargetEntityVersion = target.Version,
                BulldozeNonce = NextBulldozeNonce(),
                RequestOnly = requestOnly
            };
            return true;
        }

        public static bool TryApplyBulldoze(BulldozeCommand command)
        {
            if (command == null)
            {
                return false;
            }

            if (command.IsMultiSelect && command.MultiTargetIndices != null && command.MultiTargetIndices.Count > 0)
            {
                bool allSucceeded = true;
                for (int i = 0; i < command.MultiTargetIndices.Count; i++)
                {
                    int index = command.MultiTargetIndices[i];
                    int version = command.MultiTargetVersions[i];
                    bool success = ApplySingleBulldoze(index, version);
                    if (!success)
                    {
                        allSucceeded = false;
                    }
                }
                return allSucceeded;
            }
            else
            {
                return ApplySingleBulldoze(command.TargetEntityIndex, command.TargetEntityVersion);
            }
        }

        private static bool ApplySingleBulldoze(int index, int version)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return false;
            }

            EntityManager entityManager = world.EntityManager;
            Entity target = new()
            {
                Index = index,
                Version = version
            };

            if (target == Entity.Null || !entityManager.Exists(target))
            {
                Log.Debug(
                    $"BulldozeService: target {index}:{version} is missing, treating as already removed.");
                return true;
            }

            Entity definition = entityManager.CreateEntity();

            Entity prefab = Entity.Null;
            if (entityManager.HasComponent<PrefabRef>(target))
            {
                prefab = entityManager.GetComponentData<PrefabRef>(target).m_Prefab;
            }

            Entity owner = Entity.Null;
            if (entityManager.HasComponent<Owner>(target))
            {
                owner = entityManager.GetComponentData<Owner>(target).m_Owner;
            }

            entityManager.AddComponentData(definition, new CreationDefinition
            {
                m_Prefab = prefab,
                m_SubPrefab = Entity.Null,
                m_Original = target,
                m_Owner = owner,
                m_Attached = Entity.Null,
                m_Flags = CreationFlags.Delete,
                m_RandomSeed = 0
            });

            if (entityManager.HasComponent<Transform>(target))
            {
                Transform transform = entityManager.GetComponentData<Transform>(target);
                entityManager.AddComponentData(definition, new ObjectDefinition
                {
                    m_Position = transform.m_Position,
                    m_LocalPosition = transform.m_Position,
                    m_Scale = new Unity.Mathematics.float3(1f, 1f, 1f),
                    m_Rotation = transform.m_Rotation,
                    m_LocalRotation = transform.m_Rotation,
                    m_Elevation = 0f,
                    m_Intensity = 1f,
                    m_Age = 0f,
                    m_ParentMesh = -1,
                    m_GroupIndex = 0,
                    m_Probability = 100,
                    m_PrefabSubIndex = -1
                });
            }

            if (prefab != Entity.Null)
            {
                entityManager.AddComponentData(definition, new PrefabRef
                {
                    m_Prefab = prefab
                });
            }

            entityManager.AddComponentData(definition, new Updated());

            Log.Debug($"BulldozeService: queued bulldoze for entity {target.Index}:{target.Version}.");
            return true;
        }

        private static int NextBulldozeNonce()
        {
            int next = Interlocked.Increment(ref _bulldozeNonceCounter);
            if (next != 0)
            {
                return next;
            }

            return Interlocked.Increment(ref _bulldozeNonceCounter);
        }
    }
}
