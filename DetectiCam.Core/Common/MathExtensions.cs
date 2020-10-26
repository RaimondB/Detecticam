using System;
using System.Collections.Generic;
using System.Text;

namespace DetectiCam.Core.Common
{
    public static class MathExtensions
    {
        public static T CropInRange<T>(this T value, T lowerBound, T upperBound) where T : IComparable<T>
        {
            if (value.CompareTo(lowerBound) < 0)
            {
                return lowerBound;
            }
            else if (value.CompareTo(upperBound) > 0)
            {
                return upperBound;
            }
            else
            {
                return value;
            }
        }
    }
}
