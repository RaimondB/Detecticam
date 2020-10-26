
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DetectiCam.Core.Detection
{
    public class Region
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }

        public override string ToString()
        {
            return $"{{Left:{Left}, Top:{Top}, Right:{Right}, Bottom:{Bottom}}}";
        }
    }
}
