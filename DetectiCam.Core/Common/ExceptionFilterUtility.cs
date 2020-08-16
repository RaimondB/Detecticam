using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DetectiCam.Core.Common
{
    public static class ExceptionFilterUtility
    {
        public static bool True(Action action)
        {
#pragma warning disable CA1062 // Validate arguments of public methods
            action();
#pragma warning restore CA1062 // Validate arguments of public methods
            return true;
        }

        public static bool False(Action action)
        {
#pragma warning disable CA1062 // Validate arguments of public methods
            action();
#pragma warning restore CA1062 // Validate arguments of public methods
            return false;
        }
    }
}
