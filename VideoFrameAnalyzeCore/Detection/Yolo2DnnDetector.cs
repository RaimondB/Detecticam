using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VideoFrameAnalyzeStd.Detection;

namespace VideoFrameAnalyzer
{
    public class Yolo2DnnDetector : IDnnDetector
    {
        string[] _outNames;
        //private static string prott1 = @"C:\Users\Raimo\Downloads\MobileNetSSD_deploy.prototxt";
        //private static string prott2 = @"C:\Users\Raimo\Downloads\mobilenet_iter_73000.caffemodel";

        //private static string prott1 = @"C:\Users\Raimo\Downloads\mobilenet_yolov3_lite_deploy.prototxt";
        //private static string prott2 = @"C:\Users\Raimo\Downloads\mobilenet_yolov3_lite_deploy.caffemodel";
        //private OpenCvSharp.Dnn.Net nnet = OpenCvSharp.Dnn.CvDnn.ReadNetFromCaffe(prott1, prott2);


        //YOLOv3
        //https://github.com/pjreddie/darknet/blob/master/cfg/yolov3.cfg
        private const string Cfg = @"C:\Users\Raimo\Downloads\yolov2.cfg";

        //https://pjreddie.com/media/files/yolov3.weights
        private const string Weight = @"C:\Users\Raimo\Downloads\yolov2.weights";

        //private static readonly string[] Labels = { "aeroplane", "bicycle", "bird", "boat", "bottle", "bus", "car", "cat", "chair", "cow", "diningtable", "dog", "horse", "motorbike", "person", "pottedplant", "sheep", "sofa", "train", "tvmonitor" };
        //random assign color to each label
        //private static readonly Scalar[] Colors = Enumerable.Repeat(false, 80).Select(x => Scalar.RandomColor()).ToArray();

        //https://github.com/pjreddie/darknet/blob/master/data/coco.names
        private const string Names = @"C:\Users\Raimo\Downloads\coco.names";
        //get labels from coco.names
        private static readonly string[] Labels = File.ReadAllLines(Names).ToArray();

        private static readonly Scalar[] Colors = Enumerable.Repeat(false, 20).Select(x => Scalar.RandomColor()).ToArray();


        private OpenCvSharp.Dnn.Net nnet;
        private readonly ILogger _logger;

        public Yolo2DnnDetector(ILogger<IDnnDetector> logger)
        {
            _logger = logger;

            nnet = OpenCvSharp.Dnn.CvDnn.ReadNetFromDarknet(Cfg, Weight);
            //nnet.SetPreferableBackend(Net.Backend.INFERENCE_ENGINE);
            //nnet.SetPreferableTarget(Net.Target.CPU);
            _outNames = nnet.GetUnconnectedOutLayersNames();
        }

        public DnnDetectedObject[] ClassifyObjects(Mat image, Rect boxToAnalyze)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            var blob = CvDnn.BlobFromImage(image, 1.0 / 255, new Size(320, 320), new Scalar(), crop: false);
            nnet.SetInput(blob);

            //create mats for output layer
            Mat[] outs = Enumerable.Repeat(false, _outNames.Length).Select(_ => new Mat()).ToArray();

            //forward model
            nnet.Forward(outs, _outNames);

            const float threshold = 0.5f;       //for confidence 
            const float nmsThreshold = 0.3f;    //threshold for nms

            var detections = ExtractYolo2Results(outs, image, threshold, nmsThreshold);
            blob.Dispose();

            foreach (var output in outs)
            {
                output.Dispose();
            }

            return detections;
        }

        private DnnDetectedObject[] ExtractYolo2Results(IEnumerable<Mat> output, Mat image, float threshold, float nmsThreshold, bool nms = true)
        {
            var classIds = new List<int>();
            var confidences = new List<float>();
            var probabilities = new List<float>();
            var boxes = new List<Rect2d>();

            var w = image.Width;
            var h = image.Height;

            var prob = output.First();

            /* YOLO2 VOC output
             0 1 : center                    2 3 : w/h
             4 : confidence                  5 ~24 : class probability */
            const int prefix = 5;   //skip 0~4

            for (int i = 0; i < prob.Rows; i++)
            {
                var confidence = prob.At<float>(i, 4);
                if (confidence > threshold)
                {
                    //get classes probability
                    Cv2.MinMaxLoc(prob.Row(i).ColRange(prefix, prob.Cols), out _, out Point max);
                    var classes = max.X;
                    var probability = prob.At<float>(i, classes + prefix);

                    if (probability > threshold) //more accuracy
                    {
                        //get center and width/height
                        var centerX = prob.At<float>(i, 0) * w;
                        var centerY = prob.At<float>(i, 1) * h;
                        var width = prob.At<float>(i, 2) * w;
                        var height = prob.At<float>(i, 3) * h;

                        float X = Math.Max(0, centerX - (width / 2.0f));
                        float Y = Math.Max(0, centerY - (height / 2.0f));

                        //put data to list for NMSBoxes
                        classIds.Add(classes);
                        confidences.Add(confidence);
                        probabilities.Add(probability);
                        boxes.Add(new Rect2d(X, Y, width, height));
                    }
                }
            }

            int[] indices;

            if (!nms)
            {
                //using non-maximum suppression to reduce overlapping low confidence box
                indices = Enumerable.Range(0, boxes.Count).ToArray();
            }
            else
            {
                CvDnn.NMSBoxes(boxes, confidences, threshold, nmsThreshold, out indices);
                _logger.LogInformation($"NMSBoxes drop {confidences.Count - indices.Length} overlapping result.");
            }

            var result = new List<DnnDetectedObject>();

            foreach (var i in indices)
            {
                var box = boxes[i];

                var detection = new DnnDetectedObject()
                {
                    Index = classIds[i],
                    Label = Labels[classIds[i]],
                    Color = Colors[classIds[i]],
                    Probability = probabilities[i],
                    BoundingBox = box
                };
                result.Add(detection);
            }

            return result.ToArray();
        }
    }
}
