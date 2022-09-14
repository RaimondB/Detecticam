using DetectiCam.Core.Detection;
using OpenCvSharp;
using System;
using DetectiCam.Core.Common;

namespace DetectiCam.Core.Visualization
{
    public static class Visualizer
    {
        public static Mat AnnotateImage(Mat orgImage, DnnDetectedObject[] detectedObjects)
        {
            if (detectedObjects == null) throw new ArgumentNullException(nameof(detectedObjects));

            Mat result = new();
            Cv2.CopyTo(orgImage, result);
            foreach (var dObj in detectedObjects)
            {
                var x1 = dObj.BoundingBox.X.CropInRange(0, (double)result.Width);
                var y1 = dObj.BoundingBox.Y.CropInRange(0, (double)result.Height);
                var w = dObj.BoundingBox.Width.CropInRange(0, (double)result.Width - x1);
                var h = dObj.BoundingBox.Height.CropInRange(0, (double)result.Height - y1);
                var color = dObj.Color;

                var label = $"{dObj.Label} {dObj.Probability * 100:0.00}%";
                var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheyTriplex, 0.5, 1, out var baseline);

                var yLabelBase = y1.CropInRange(baseline, y1 - baseline);
                var yLabelTop = y1.CropInRange(0, y1 - textSize.Height - baseline);

                //draw result boundingbox
                result.Rectangle(new Point(x1, y1), new Point(x1 + w, y1 + h), color, 2);

                //draw result label on top of boundingbox
                Cv2.Rectangle(result, new Rect(new Point(x1, yLabelTop),
                    new Size(textSize.Width, textSize.Height + baseline)), color, Cv2.FILLED);
                Cv2.PutText(result, label, new Point(x1, yLabelBase),
                    HersheyFonts.HersheyTriplex, 0.5, Scalar.Black);

            }
            return result;
        }
    }
}
