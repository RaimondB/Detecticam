using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Text;
using Range = OpenCvSharp.Range;

namespace DetectiCam.Core.Detection
{
    public static class OpenCVExtensions
    {
        public static int FindMaxValueIndexInRange<T>(this Mat inputMatrix, int dim0Index, int dim1Index, Range dim2Range) where T:unmanaged,IComparable<T>
        {
            if (inputMatrix is null) throw new ArgumentNullException(nameof(inputMatrix));

            int dim2MaxIndex = -1;
            T dim2MaxValue = default;

            for (int dim2Index = dim2Range.Start; dim2Index <= dim2Range.End; dim2Index++)
            {
                var curValue = inputMatrix.At<T>(dim0Index, dim1Index, dim2Index);
                if (curValue.CompareTo(dim2MaxValue) >0)
                {
                    dim2MaxIndex = dim2Index;
                    dim2MaxValue = curValue;
                }
            }
            return dim2MaxIndex;
        }
    }
}
