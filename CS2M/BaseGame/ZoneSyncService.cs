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
    internal static class ZoneSyncService
    {
        private static readonly Dictionary<string, ZonePrefab> PrefabCache = new(StringComparer.Ordinal);
        private static int _applyNonceCounter;

        /// <summary>Server-replicated commands to be applied on the client during ZoneToolSystem.OnUpdate.</summary>
        internal static readonly ConcurrentQueue<ZoneApplyCommand> PendingReplays = new();
        /// <summary>Client requests to be applied on the server during ZoneToolSystem.OnUpdate.</summary>
        internal static readonly ConcurrentQueue<ZoneApplyCommand> PendingServerRequests = new();

        public static bool IsSupportedOperation(ZoneToolSystem tool, out string reason)
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
            ZoneToolSystem tool,
            bool singleFrameOnly,
            bool requestOnly,
            out ZoneApplyCommand command)
        {
            command = null;
            if (!IsSupportedOperation(tool, out _))
            {
                return false;
            }

            ControlPoint startPoint = default;
            object spObj = ReflectionHelper.GetAttr(tool, "m_StartPoint");
            if (spObj is ControlPoint spCp) startPoint = spCp;

            ControlPoint snapPoint = GetSnapPoint(tool);

            ControlPoint raycastPoint = default;
            object rpObj = ReflectionHelper.GetAttr(tool, "m_RaycastPoint");
            if (rpObj is ControlPoint rpCp) raycastPoint = rpCp;

            int preApplyState = 0;
            object stateObj = ReflectionHelper.GetAttr(tool, "m_State");
            if (stateObj != null)
            {
                preApplyState = Convert.ToInt32(stateObj);
            }

            command = new ZoneApplyCommand
            {
                PrefabName = tool.prefab.name,
                Mode = (int)tool.mode,
                Overwrite = tool.overwrite,
                PreApplyState = preApplyState,
                SingleFrameOnly = singleFrameOnly,
                ApplyNonce = NextApplyNonce(),
                RequestOnly = requestOnly,
                StartPoint = ToSnapshot(startPoint),
                SnapPoint = ToSnapshot(snapPoint),
                RaycastPoint = ToSnapshot(raycastPoint)
            };
            return true;
        }

        public static bool TryReplayApply(ZoneApplyCommand command)
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

            ZoneToolSystem tool = world.GetExistingSystemManaged<ZoneToolSystem>();
            PrefabSystem prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
            if (tool == null || prefabSystem == null)
            {
                return false;
            }

            if (!TryResolveZonePrefab(prefabSystem, command.PrefabName, out ZonePrefab prefab))
            {
                Log.Warn($"ZoneSync: zone prefab '{command.PrefabName}' not found.");
                return false;
            }

            bool setPrefab = tool.TrySetPrefab(prefab);
            if (!setPrefab)
            {
                tool.prefab = prefab;
            }

            tool.mode = (ZoneToolSystem.Mode)command.Mode;
            tool.overwrite = command.Overwrite;

            ControlPoint startPoint = FromSnapshot(command.StartPoint);
            ControlPoint snapPoint = FromSnapshot(command.SnapPoint);
            ControlPoint raycastPoint = FromSnapshot(command.RaycastPoint);
            int replayState = command.PreApplyState;

            if (command.Mode == (int)ZoneToolSystem.Mode.Marquee)
            {
                if (replayState == 0 && !command.SingleFrameOnly)
                {
                    startPoint = snapPoint;
                    replayState = 1;
                }
                else if (replayState != 0)
                {
                    startPoint = default;
                    replayState = 0;
                }
            }

            ReflectionHelper.SetAttr(tool, "m_StartPoint", startPoint);
            ReflectionHelper.SetAttr(tool, "m_RaycastPoint", raycastPoint);

            object stateObj = ReflectionHelper.GetAttr(tool, "m_State");
            if (stateObj != null)
            {
                Type enumType = stateObj.GetType();
                object enumValue = Enum.ToObject(enumType, replayState);
                ReflectionHelper.SetAttr(tool, "m_State", enumValue);
            }

            SetSnapPoint(tool, snapPoint);

            JobHandle handle = default;

            object snapHandleObj = ReflectionHelper.Call(
                tool,
                "SnapPoint",
                new[] { typeof(JobHandle) },
                handle);
            if (snapHandleObj is JobHandle snapHandle)
            {
                handle = snapHandle;
            }

            object updateHandleObj = ReflectionHelper.Call(
                tool,
                "UpdateDefinitions",
                new[] { typeof(JobHandle) },
                handle);
            if (updateHandleObj is JobHandle updateHandle)
            {
                handle = updateHandle;
            }

            handle.Complete();
            return true;
        }

        private static ZoneControlPointSnapshot ToSnapshot(ControlPoint point)
        {
            return new ZoneControlPointSnapshot
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

        private static ControlPoint FromSnapshot(ZoneControlPointSnapshot snapshot)
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

        private static ControlPoint GetSnapPoint(ZoneToolSystem tool)
        {
            object snapValue = ReflectionHelper.GetAttr(tool, "m_SnapPoint");
            if (snapValue == null)
            {
                return default;
            }

            try
            {
                return ReflectionHelper.GetProp<ControlPoint>(snapValue, "value");
            }
            catch
            {
                try
                {
                    return ReflectionHelper.Call<ControlPoint>(snapValue, "get_value");
                }
                catch
                {
                    return default;
                }
            }
        }

        private static void SetSnapPoint(ZoneToolSystem tool, ControlPoint point)
        {
            object snapValue = ReflectionHelper.GetAttr(tool, "m_SnapPoint");
            if (snapValue == null)
            {
                return;
            }

            try
            {
                ReflectionHelper.Call(snapValue, "set_value", point);
                ReflectionHelper.SetAttr(tool, "m_SnapPoint", snapValue);
            }
            catch (Exception ex)
            {
                Log.Warn($"ZoneSync: failed to set snap point: {ex}");
            }
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

        private static bool TryResolveZonePrefab(PrefabSystem prefabSystem, string prefabName, out ZonePrefab prefab)
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
                .OfType<ZonePrefab>()
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
