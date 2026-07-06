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
            while (_frameSamples.Count > 0 && 
                   _frameSamples.Peek().Frame <= GetEffectiveFrame())
            {
                var sample = _frameSamples.Dequeue();
                UpdateFrameFromSample(sample);
            }
        }

        private void SmoothInterpolation()
        {
            if (_frameSamples.Count == 0)
                return;

            var latestSample = _frameSamples.Last();
            uint effectiveFrame = GetEffectiveFrame();
            
            // Calculate how far behind we are
            int frameDiff = (int)(latestSample.Frame - effectiveFrame);
            
            if (frameDiff > 0 && frameDiff <= MAX_HISTORY_SIZE)
            {
                // We have enough history, interpolate
                _interpolationTarget = latestSample.Frame;
                _interpolationCurrent = Mathf.Lerp(
                    _interpolationCurrent,
                    _interpolationTarget,
                    INTERPOLATION_SPEED * UnityEngine.Time.deltaTime
                );
            }
            else if (frameDiff > MAX_HISTORY_SIZE)
            {
                // Too far behind, snap to latest
                SnapToFrame(latestSample.Frame);
            }
        }

        private void UpdateFrameFromSample(FrameSample sample)
        {
            uint currentFrame = GetEffectiveFrame();
            float frameDelta = Math.Abs(currentFrame - sample.Frame);
            
            // Check for unreasonable jumps — but always accept the first sample (client starts at 0)
            if (frameDelta > MAX_REASONABLE_FRAME_DELTA && currentFrame != 0)
            {
                Log.Warn($"Frame sync detected unusual jump: {currentFrame} -> {sample.Frame}");
                // Don't accept this sample
                return;
            }
            
            // Apply frame value
            SetEffectiveFrame(sample.Frame);
            _interpolationCurrent = sample.Frame;
            
            // Track latency based on timestamp
            long currentTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            double latencySeconds = (currentTimestamp - sample.Timestamp) / (double)System.Diagnostics.Stopwatch.Frequency;
            double latencyMs = latencySeconds * 1000.0;
            
            // Smooth average latency
            _avgLatency = Mathf.Lerp(_avgLatency, (float)latencyMs, 0.1f);
            
            Log.Trace($"Frame updated to {sample.Frame}, latency: {latencyMs:F1}ms");
        }

        private uint GetEffectiveFrame()
        {
            // Return interpolated frame for smooth rendering
            if (Command.CurrentRole == MultiplayerRole.Client && _frameSamples.Count > 0)
            {
                return (uint)_interpolationCurrent;
            }
            return _simulationSystem.frameIndex;
        }

        private void SetEffectiveFrame(uint frame)
        {
            _simulationSystem.SetPrivateProperty("frameIndex", frame);
        }

        private void SnapToFrame(uint targetFrame)
        {
            uint currentFrame = _simulationSystem.frameIndex;
            if (Mathf.Abs((int)currentFrame - (int)targetFrame) > 120)
            {
                Log.Warn($"Snapping frame from {currentFrame} to {targetFrame}");
                SetEffectiveFrame(targetFrame);
                _interpolationCurrent = targetFrame;
            }
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
