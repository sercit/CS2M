using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Unity.Entities;

namespace CS2M.Commands.Handler.BaseGame
{
    public class FrameCommandHandler : ClientCommandHandler<FrameCommand>
    {
        private const int MAX_FRAME_JUMP = 300;
        private uint _lastProcessedFrame = 0;

        protected override void OnValidatedCommand(FrameCommand command)
        {
            if (!IsValidFrameJump(command.Frame))
            {
                Log.Warn($"Frame jump too large: {_lastProcessedFrame} -> {command.Frame}");
                return;
            }

            var frameSystem = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<global::CS2M.BaseGame.Systems.FrameSyncSystem>();
            if (frameSystem != null)
            {
                frameSystem.ReceiveFrameUpdate(command);
                _lastProcessedFrame = command.Frame;
            }
        }

        private bool IsValidFrameJump(uint newFrame)
        {
            if (_lastProcessedFrame == 0)
                return true;

            int diff = (int)(newFrame - _lastProcessedFrame);

            if (diff >= 0 && diff <= MAX_FRAME_JUMP)
                return true;

            if (diff > -50 && diff < 0)
                return true;

            return false;
        }
    }
}
