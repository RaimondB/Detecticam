using System;
using System.Collections.Generic;
using System.Text;

namespace DetectiCam.Core.Pipeline
{
    public interface ISyncTokenProvider
    {
        int? SyncToken { get; }
    }
}
