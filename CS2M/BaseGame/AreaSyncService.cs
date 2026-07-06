using System;
using System.Collections.Concurrent;
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
    internal static class AreaSyncService
    {
        private static readonly Dictionary<string, AreaPrefab> PrefabCache = new(StringComparer.Ordinal);
        private static int _applyNonceCounter;

        /// <summary>Commands queued by the network handler to be replayed during the next ECS OnUpdate of AreaToolSystem.</summary>
        internal static readonly ConcurrentQueue<AreaApplyCommand> PendingReplays = new();

        public static bool IsSupportedOperation(AreaToolSystem tool, out string reason)
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
            AreaToolSystem tool,
            bool singleFrameOnly,
            bool requestOnly,
            out AreaApplyCommand command)
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

            AreaControlPointSnapshot[] controlPoints = CaptureControlPoints(tool);

            command = new AreaApplyCommand
            {
                PrefabName = tool.prefab.name,
                Mode = (int)tool.mode,
                Underground = tool.underground,
                AllowGenerate = tool.allowGenerate,
                PreApplyState = preApplyState,
                SingleFrameOnly = singleFrameOnly,
                ApplyNonce = NextApplyNonce(),
                RequestOnly = requestOnly,
                ControlPoints = controlPoints
            };
            return true;
        }

        public static bool TryReplayApply(AreaApplyCommand command)
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

            AreaToolSystem tool = world.GetExistingSystemManaged<AreaToolSystem>();
            PrefabSystem prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
            if (tool == null || prefabSystem == null)
            {
                return false;
            }

            if (!TryResolveAreaPrefab(prefabSystem, command.PrefabName, out AreaPrefab prefab))
            {
                Log.Warn($"AreaSync: area prefab '{command.PrefabName}' not found.");
                return false;
            }

            bool setPrefab = tool.TrySetPrefab(prefab);
            if (!setPrefab)
            {
                tool.prefab = prefab;
            }

            tool.mode = (AreaToolSystem.Mode)command.Mode;
            tool.underground = command.Underground;

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
                Log.Warn($"AreaSync: replay apply failed for nonce {command.ApplyNonce}: {ex}");
                return false;
            }
        }

        private static AreaControlPointSnapshot[] CaptureControlPoints(AreaToolSystem tool)
        {
            object listObj = ReflectionHelper.GetAttr(tool, "m_ControlPoints");
            if (listObj == null)
            {
                return Array.Empty<AreaControlPointSnapshot>();
            }

            int length = ReflectionHelper.Call<int>(listObj, "get_Length");
            if (length <= 0)
            {
                return Array.Empty<AreaControlPointSnapshot>();
            }

            var snapshots = new AreaControlPointSnapshot[length];
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

        private static void RestoreControlPoints(AreaToolSystem tool, AreaControlPointSnapshot[] snapshots)
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

        private static AreaControlPointSnapshot ToSnapshot(ControlPoint point)
        {
            return new AreaControlPointSnapshot
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

        private static ControlPoint FromSnapshot(AreaControlPointSnapshot snapshot)
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
            return Interlocked.Increment(ref _applyNonceCounter);
        }

        private static bool TryResolveAreaPrefab(PrefabSystem prefabSystem, string prefabName, out AreaPrefab prefab)
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
                .OfType<AreaPrefab>()
                .FirstOrDefault(p => string.Equals(p.name, prefabName, StringComparison.Ordinal));
            if (prefab == null)
            {
                return false;
            }

            PrefabCache[prefabName] = prefab;
            return true;
        }
    }
}
