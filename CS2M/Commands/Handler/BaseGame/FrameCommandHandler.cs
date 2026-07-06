using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using LiteNetLib;
using System.Collections.Generic;
using Unity.Entities;

namespace CS2M.Commands.Handler.BaseGame
{
    /// <summary>
    ///     Handles frame sync commands with anti-cheat and validation
    /// </summary>
    public class FrameCommandHandler : ClientCommandHandler<FrameCommand>
    {
        private const int MAX_FRAME_JUMP = 300; // Maximum frames allowed in single update
        private uint _lastProcessedFrame = 0;
        private readonly List<long> _processingTimes = new();
        private const int MAX_PROCESSING_HISTORY = 60;

        protected override void OnValidatedCommand(FrameCommand command)
        {
            try
            {
                Log.Trace($"Processing frame sync: {command.Frame}");

                // Validate frame doesn't jump too far ahead
                if (!IsValidFrameJump(command.Frame))
                {
                    Log.Warn($"Frame jump too large: {_lastProcessedFrame} -> {command.Frame}");
                    return;
                }

                // Update processing times for performance monitoring
                RecordProcessingTime();

                // Apply frame through sync system
                var frameSystem = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<global::CS2M.BaseGame.Systems.FrameSyncSystem>();
                if (frameSystem != null)
                {
                    frameSystem.ReceiveFrameUpdate(command);
                    _lastProcessedFrame = command.Frame;
                    
                    Log.Trace($"Frame accepted: {command.Frame}, last: {_lastProcessedFrame}");
                }
                else
                {
                    Log.Error("FrameSyncSystem not available, cannot process frame");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"Failed to process frame update: {ex.Message}", ex);
            }
        }

        private bool IsValidFrameJump(uint newFrame)
        {
            // First frame ever received — always accept as the baseline.
            if (_lastProcessedFrame == 0)
                return true;

            int diff = (int)(newFrame - _lastProcessedFrame);

            // Accept normal progression
            if (diff >= 0 && diff <= MAX_FRAME_JUMP)
                return true;

            // Accept small jumps backward (network reordering)
            if (diff > -50 && diff < 0)
            {
                Log.Debug($"Small backward frame detected: {diff}, accepting");
                return true;
            }

            return false;
        }

        private void RecordProcessingTime()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            _processingTimes.Add(now);

            if (_processingTimes.Count > MAX_PROCESSING_HISTORY)
            {
                _processingTimes.RemoveAt(0);
            }

            if (_processingTimes.Count >= 10)
            {
                double avgProcessingMs = CalculateAverageProcessingTime();
                
                if (avgProcessingMs > 50) // Warn if processing takes too long
                {
                    Log.Warn($"High frame processing overhead: {avgProcessingMs:F1}ms average");
                }
            }
        }

        private double CalculateAverageProcessingTime()
        {
            if (_processingTimes.Count < 2)
                return 0;

            long frequency = System.Diagnostics.Stopwatch.Frequency;
            long totalTicks = 0;
            
            for (int i = 0; i < _processingTimes.Count - 1; i++)
            {
                totalTicks += _processingTimes[i + 1] - _processingTimes[i];
            }

            return ((double)totalTicks / _processingTimes.Count) * 1000.0 / frequency;
        }
    }
}
