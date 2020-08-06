using System;
using System.Collections.Generic;
using System.Text;

namespace DetectiCam.Core.Pipeline
{
    public interface ITimestampTrigger
    {
        void ExecuteTrigger(DateTime timestamp, int triggerId);
    }
}
