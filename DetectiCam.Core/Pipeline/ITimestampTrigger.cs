using System;

namespace DetectiCam.Core.Pipeline
{
    public interface ITimestampTrigger
    {
        void ExecuteTrigger(DateTime timestamp, int triggerId);
    }
}
