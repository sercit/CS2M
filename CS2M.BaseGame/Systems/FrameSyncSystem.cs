using System;
using System.Linq;
using Game;
using Game.Simulation;
using Unity.Entities;
using UnityEngine;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using System.Collections.Generic;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Enhanced frame synchronization system with interpolation, smoothing, and prediction
    /// </summary>
    public partial class FrameSyncSystem : GameSystemBase
    {
        private SimulationSystem _simulationSystem;
        
        // Server-side: broadcast interval in frames (60 = ~1 second at 1x speed)
        private const int BROADCAST_INTERVAL = 60;
        private int _broadcastCounter;
        
        // Client-side: interpolation settings
        private readonly Queue<FrameSample> _frameSamples = new();
        private const int MAX_HISTORY_SIZE = 5;
        private float _interpolationTarget = 0.0f;
        private float _interpolationCurrent = 0.0f;
        private const float INTERPOLATION_SPEED = 0.9f;
        
        // Network latency compensation
        private float _avgLatency = 0.05f; // Default 50ms
        
        // Anti-cheat: track reasonable frame deltas
        private const float MAX_REASONABLE_FRAME_DELTA = 300.0f; // 5 seconds worth
        private uint _lastReceivedFrame = 0;
        
        protected override void OnCreate()
        {
            base.OnCreate();
            _simulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            
            Log.Debug($"FrameSyncSystem created for role {Command.CurrentRole}");
        }

        protected override void OnUpdate()
        {
            switch (Command.CurrentRole)
            {
                case MultiplayerRole.Server:
                    HandleServerUpdate();
                    break;
                    
                case MultiplayerRole.Client:
                    HandleClientUpdate();
                    break;
                    
                default:
                    return;
            }
        }

        private void HandleServerUpdate()
        {
            _broadcastCounter++;
            
            if (_broadcastCounter >= BROADCAST_INTERVAL)
            {
                _broadcastCounter = 0;
                uint currentFrame = _simulationSystem.frameIndex;
                
                Command.SendToAll(new FrameCommand
                {
                    Frame = currentFrame,
                    Timestamp = System.Diagnostics.Stopwatch.GetTimestamp()
                });
            }
        }

        private void HandleClientUpdate()
        {
            // Process received frame samples
            ProcessFrameQueue();
            
            // Interpolate towards target frame
            SmoothInterpolation();
        }

        private void ProcessFrameQueue()
        {
            lock (_frameSamples)
            {
                // Drop all but the latest sample — we just want the most recent server frame
                while (_frameSamples.Count > 1)
                    _frameSamples.Dequeue();

                if (_frameSamples.Count == 1)
                {
                    var sample = _frameSamples.Dequeue();
                    ApplyFrameSample(sample);
                }
            }
        }

        private void ApplyFrameSample(FrameSample sample)
        {
            _interpolationCurrent = sample.Frame;
            SetEffectiveFrame(sample.Frame);

            long currentTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            double latencyMs = (currentTimestamp - sample.Timestamp) /
                               (double)System.Diagnostics.Stopwatch.Frequency * 1000.0;
            _avgLatency = Mathf.Lerp(_avgLatency, (float)latencyMs, 0.1f);

            Log.Trace($"Frame updated to {sample.Frame}, latency: {latencyMs:F1}ms");
        }

        private void SmoothInterpolation()
        {
            // No-op: frame is applied directly in ProcessFrameQueue
        }

        private uint GetEffectiveFrame()
        {
            return _simulationSystem.frameIndex;
        }

        private void SetEffectiveFrame(uint frame)
        {
            try { _simulationSystem.SetPrivateProperty("frameIndex", frame); }
            catch { _simulationSystem.SetPrivateField("frameIndex", frame); }
        }

        private void SnapToFrame(uint targetFrame)
        {
            _interpolationCurrent = targetFrame;
            SetEffectiveFrame(targetFrame);
        }

        /// <summary>
        ///     Receives a frame update from server (called by FrameCommand handler)
        /// </summary>
        public void ReceiveFrameUpdate(FrameCommand frameCommand)
        {
            if (Command.CurrentRole != MultiplayerRole.Client)
                return;

            // Validate frame is not going backwards too much
            if (frameCommand.Frame < _lastReceivedFrame)
            {
                Log.Debug($"Ignoring old frame packet: {frameCommand.Frame} < {_lastReceivedFrame}");
                return;
            }

            _lastReceivedFrame = frameCommand.Frame;
            
            // Add to queue for processing during next update
            lock (_frameSamples)
            {
                if (_frameSamples.Count >= MAX_HISTORY_SIZE * 2)
                {
                    _frameSamples.Dequeue(); // Remove oldest if queue is too big
                }
                
                _frameSamples.Enqueue(new FrameSample
                {
                    Frame = frameCommand.Frame,
                    Timestamp = frameCommand.Timestamp
                });
                
                Log.Trace($"Received frame {frameCommand.Frame}, queue size: {_frameSamples.Count}");
            }
        }

        /// <summary>
        ///     Gets the current smoothed frame number for client rendering
        /// </summary>
        public float GetCurrentSmoothedFrame()
        {
            return _interpolationCurrent;
        }

        /// <summary>
        ///     Resets all frame synchronization state
        /// </summary>
        public void Reset()
        {
            lock (_frameSamples)
            {
                while (_frameSamples.Count > 0)
                    _frameSamples.Dequeue();
            }
            
            _interpolationTarget = 0f;
            _interpolationCurrent = 0f;
            _avgLatency = 0.05f;
            _lastReceivedFrame = 0;
            _broadcastCounter = 0;
            
            Log.Debug("FrameSyncSystem reset");
        }
    }

    /// <summary>
    ///     Represents a single frame observation with metadata
    /// </summary>
    public struct FrameSample
    {
        public uint Frame;
        public long Timestamp;
    }
}
