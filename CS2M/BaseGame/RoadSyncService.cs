using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CS2M.BaseGame.Commands;
using CS2M.Helpers;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CS2M.BaseGame
{
    internal static class RoadSyncService
    {
        private static readonly Dictionary<string, NetPrefab> PrefabCache = new(StringComparer.Ordinal);
        private static int _applyNonceCounter;

        public static bool IsSupportedOperation(NetToolSystem tool, out string reason)
        {
            reason = null;
            if (tool?.prefab == null)
            {
                reason = "missing prefab";
                return false;
            }

            return true;
        }

        public static bool TryBuildApplyCommand(
            NetToolSystem tool,
            bool singleFrameOnly,
            bool requestOnly,
            out RoadApplyCommand command)
        {
            command = null;
            if (!IsSupportedOperation(tool, out _))
            {
                return false;
            }

            int preApplyState = 0;
            object stateObj = ReflectionHelper.GetAttr(tool, "m_State");
            if (stateObj != null)
            {
                preApplyState = Convert.ToInt32(stateObj);
            }

            int randomSeed = 0;
            try { randomSeed = Convert.ToInt32(ReflectionHelper.GetAttr(tool, "m_RandomSeed")); } catch { }

            ControlPoint applyStartPoint = default;
            object aspObj = ReflectionHelper.GetAttr(tool, "m_ApplyStartPoint");
            if (aspObj is ControlPoint aspCp) applyStartPoint = aspCp;

            ControlPoint lastRaycastPoint = default;
            object lrpObj = ReflectionHelper.GetAttr(tool, "m_LastRaycastPoint");
            if (lrpObj is ControlPoint lrpCp) lastRaycastPoint = lrpCp;
            RoadControlPointSnapshot[] controlPoints = CaptureControlPoints(tool);

            command = new RoadApplyCommand
            {
                PrefabName = tool.prefab.name,
                Mode = (int)tool.mode,
                Elevation = tool.elevation,
                ParallelCount = tool.parallelCount,
                ParallelOffset = tool.parallelOffset,
                Underground = tool.underground,
                SelectedSnap = (int)tool.selectedSnap,
                UpgradeOnly = tool.upgradeOnly,
                ServiceUpgrade = tool.serviceUpgrade,
                PreApplyState = preApplyState,
                SingleFrameOnly = singleFrameOnly,
                ApplyNonce = NextApplyNonce(),
                RandomSeed = randomSeed,
                RequestOnly = requestOnly,
                ApplyStartPoint = ToSnapshot(applyStartPoint),
                LastRaycastPoint = ToSnapshot(lastRaycastPoint),
                ControlPoints = controlPoints
            };
            return true;
        }

        public static bool TryReplayApply(RoadApplyCommand command)
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

            NetToolSystem tool = world.GetExistingSystemManaged<NetToolSystem>();
            PrefabSystem prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
            if (tool == null || prefabSystem == null)
            {
                return false;
            }

            if (!TryResolveNetPrefab(prefabSystem, command.PrefabName, out NetPrefab prefab))
            {
                Log.Warn($"RoadSync: net prefab '{command.PrefabName}' not found.");
                return false;
            }

            bool setPrefab = tool.TrySetPrefab(prefab);
            if (!setPrefab)
            {
                tool.prefab = prefab;
            }

            tool.mode = (NetToolSystem.Mode)command.Mode;
            tool.elevation = command.Elevation;
            tool.parallelCount = command.ParallelCount;
            tool.parallelOffset = command.ParallelOffset;
            tool.underground = command.Underground;
            tool.selectedSnap = (Snap)command.SelectedSnap;

            ReflectionHelper.SetAttr(tool, "m_ApplyStartPoint", FromSnapshot(command.ApplyStartPoint));
            ReflectionHelper.SetAttr(tool, "m_LastRaycastPoint", FromSnapshot(command.LastRaycastPoint));
            SetRandomSeed(tool, command.RandomSeed);

            object stateObj = ReflectionHelper.GetAttr(tool, "m_State");
            if (stateObj != null)
            {
                Type enumType = stateObj.GetType();
                object enumValue = Enum.ToObject(enumType, command.PreApplyState);
                ReflectionHelper.SetAttr(tool, "m_State", enumValue);
            }

            RestoreControlPoints(tool, command.ControlPoints);

            try
            {
                using (ReplayScope.BeginReplayScope())
                {
                    JobHandle handle = default;
                    object updateHandleObj = ReflectionHelper.Call(
                        tool,
                        "Update",
                        new[] { typeof(JobHandle), typeof(bool) },
                        handle,
                        command.SingleFrameOnly);
                    if (updateHandleObj is JobHandle updateHandle)
                    {
                        handle = updateHandle;
                    }

                    object applyHandleObj = ReflectionHelper.Call(
                        tool,
                        "Apply",
                        new[] { typeof(JobHandle), typeof(bool) },
                        handle,
                        command.SingleFrameOnly);
                    if (applyHandleObj is JobHandle applyHandle)
                    {
                        handle = applyHandle;
                    }

                    handle.Complete();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"RoadSync: replay apply failed for nonce {command.ApplyNonce}: {ex}");
                return false;
            }
        }

        private static RoadControlPointSnapshot[] CaptureControlPoints(NetToolSystem tool)
        {
            object listObj = ReflectionHelper.GetAttr(tool, "m_ControlPoints");
            if (listObj == null)
            {
                return Array.Empty<RoadControlPointSnapshot>();
            }

            int length = ReflectionHelper.Call<int>(listObj, "get_Length");
            if (length <= 0)
            {
                return Array.Empty<RoadControlPointSnapshot>();
            }

            var snapshots = new RoadControlPointSnapshot[length];
            for (int i = 0; i < length; i++)
            {
                object pointObj = ReflectionHelper.Call(
                    listObj,
                    "get_Item",
                    new[] { typeof(int) },
                    i);
                ControlPoint point = pointObj is ControlPoint cp ? cp : default;
                snapshots[i] = ToSnapshot(point);
            }

            return snapshots;
        }

        private static void RestoreControlPoints(NetToolSystem tool, RoadControlPointSnapshot[] snapshots)
        {
            object listObj = ReflectionHelper.GetAttr(tool, "m_ControlPoints");
            if (listObj == null)
            {
                return;
            }

            int length = snapshots?.Length ?? 0;
            ReflectionHelper.Call(
                listObj,
                "ResizeUninitialized",
                new[] { typeof(int) },
                length);

            for (int i = 0; i < length; i++)
            {
                ControlPoint point = FromSnapshot(snapshots[i]);
                ReflectionHelper.Call(
                    listObj,
                    "set_Item",
                    new[] { typeof(int), typeof(ControlPoint) },
                    i,
                    point);
            }

            ReflectionHelper.SetAttr(tool, "m_ControlPoints", listObj);
        }

        private static RoadControlPointSnapshot ToSnapshot(ControlPoint point)
        {
            return new RoadControlPointSnapshot
            {
                PositionX = point.m_Position.x,
                PositionY = point.m_Position.y,
                PositionZ = point.m_Position.z,
                HitPositionX = point.m_HitPosition.x,
                HitPositionY = point.m_HitPosition.y,
                HitPositionZ = point.m_HitPosition.z,
                DirectionX = point.m_Direction.x,
                DirectionY = point.m_Direction.y,
                HitDirectionX = point.m_HitDirection.x,
                HitDirectionY = point.m_HitDirection.y,
                HitDirectionZ = point.m_HitDirection.z,
                RotationX = point.m_Rotation.value.x,
                RotationY = point.m_Rotation.value.y,
                RotationZ = point.m_Rotation.value.z,
                RotationW = point.m_Rotation.value.w,
                CurvePosition = point.m_CurvePosition,
                Elevation = point.m_Elevation,
                ElementIndexX = point.m_ElementIndex.x,
                ElementIndexY = point.m_ElementIndex.y,
                SnapPriorityX = point.m_SnapPriority.x,
                SnapPriorityY = point.m_SnapPriority.y,
                OriginalEntityIndex = point.m_OriginalEntity.Index,
                OriginalEntityVersion = point.m_OriginalEntity.Version
            };
        }

        private static ControlPoint FromSnapshot(RoadControlPointSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return default;
            }

            return new ControlPoint
            {
                m_Position = new float3(snapshot.PositionX, snapshot.PositionY, snapshot.PositionZ),
                m_HitPosition = new float3(snapshot.HitPositionX, snapshot.HitPositionY, snapshot.HitPositionZ),
                m_Direction = new float2(snapshot.DirectionX, snapshot.DirectionY),
                m_HitDirection = new float3(snapshot.HitDirectionX, snapshot.HitDirectionY, snapshot.HitDirectionZ),
                m_Rotation = new quaternion(snapshot.RotationX, snapshot.RotationY, snapshot.RotationZ, snapshot.RotationW),
                m_CurvePosition = snapshot.CurvePosition,
                m_Elevation = snapshot.Elevation,
                m_ElementIndex = new int2(snapshot.ElementIndexX, snapshot.ElementIndexY),
                m_SnapPriority = new float2(snapshot.SnapPriorityX, snapshot.SnapPriorityY),
                m_OriginalEntity = new Entity
                {
                    Index = snapshot.OriginalEntityIndex,
                    Version = snapshot.OriginalEntityVersion
                }
            };
        }

        private static int NextApplyNonce()
        {
            int next = Interlocked.Increment(ref _applyNonceCounter);
            if (next != 0)
            {
                return next;
            }

            return Interlocked.Increment(ref _applyNonceCounter);
        }

        private static bool TryResolveNetPrefab(PrefabSystem prefabSystem, string prefabName, out NetPrefab prefab)
        {
            if (PrefabCache.TryGetValue(prefabName, out prefab) && prefab != null)
            {
                return true;
            }

            IEnumerable<PrefabBase> prefabs = ReflectionHelper.GetProp<IEnumerable<PrefabBase>>(prefabSystem, "prefabs");
            if (prefabs == null)
            {
                prefab = null;
                return false;
            }

            prefab = prefabs
                .OfType<NetPrefab>()
                .FirstOrDefault(p => string.Equals(p.name, prefabName, StringComparison.Ordinal));
            if (prefab == null)
            {
                return false;
            }

            PrefabCache[prefabName] = prefab;
            return true;
        }

        private static void SetRandomSeed(NetToolSystem tool, int seedValue)
        {
            object existing = ReflectionHelper.GetAttr(tool, "m_RandomSeed");
            if (existing == null)
            {
                return;
            }

            Type seedType = existing.GetType();
            if (seedType == typeof(int))
            {
                ReflectionHelper.SetAttr(tool, "m_RandomSeed", seedValue);
                return;
            }

            // Game.Common.RandomSeed is a struct — create instance and set its int field
            object newSeed = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(seedType);
            foreach (string fname in new[] { "value", "m_Value", "Value", "seed", "m_Seed" })
            {
                System.Reflection.FieldInfo f = seedType.GetField(fname,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (f != null)
                {
                    f.SetValue(newSeed, seedValue);
                    ReflectionHelper.SetAttr(tool, "m_RandomSeed", newSeed);
                    return;
                }
            }

            Log.Warn($"RoadSync: could not set m_RandomSeed — unknown field layout for {seedType}.");
        }
    }
}
