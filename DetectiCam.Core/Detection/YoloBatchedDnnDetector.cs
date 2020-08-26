using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Range = OpenCvSharp.Range;

namespace DetectiCam.Core.Detection
{
    public sealed class YoloBatchedDnnDetector : IBatchedDnnDetector
    {
        private readonly ILogger _logger;

        //YOLOv3
        //https://github.com/pjreddie/darknet/blob/master/cfg/yolov3.cfg
        private readonly string Cfg;

        //https://pjreddie.com/media/files/yolov3.weights
        private readonly string Weight;

        //https://github.com/pjreddie/darknet/blob/master/data/coco.names
        private readonly string Names;

        private readonly Scalar[] Colors;
        private readonly string[] Labels;
        private readonly OpenCvSharp.Dnn.Net nnet;
        private readonly Mat[] outs;
        private readonly string[] _outNames;

        private const float threshold = 0.5f;       //for confidence 
        private const float nmsThreshold = 0.3f;    //threshold for nms

        public YoloBatchedDnnDetector(ILogger<YoloBatchedDnnDetector> logger, IConfiguration configuration)
        {
            if (configuration is null) throw new ArgumentNullException(nameof(configuration));
            if (logger is null) throw new ArgumentNullException(nameof(logger));

            _logger = logger;

            var options = new Yolo3Options();
            configuration.GetSection(Yolo3Options.Yolo3).Bind(options);

            Cfg = Path.Combine(options.RootPath, options.ConfigFile);
            Weight = Path.Combine(options.RootPath, options.WeightsFile);
            Names = Path.Combine(options.RootPath, options.NamesFile);

            //random assign color to each label
            Labels = File.ReadAllLines(Names).ToArray();

            //get labels from coco.names
            Colors = Enumerable.Repeat(false, Labels.Length).Select(x => Scalar.RandomColor()).ToArray();

            _logger.LogInformation("Loading Neural Net");
            nnet = OpenCvSharp.Dnn.CvDnn.ReadNetFromDarknet(Cfg, Weight);
            //nnet.SetPreferableBackend(Net.Backend.INFERENCE_ENGINE);
            //nnet.SetPreferableTarget(Net.Target.OPENCL);
            _outNames = nnet.GetUnconnectedOutLayersNames()!;

            outs = Enumerable.Repeat(false, _outNames.Length).Select(_ => new Mat()).ToArray();
            _logger.LogInformation("Warm Up Neural Net with Dummy images");

            //Initialize();
        }

        public void Initialize()
        {
            _logger.LogInformation("Start Detector initalize");
            using Mat dummy1 = new Mat(320, 320, MatType.CV_8UC3, new Scalar(0, 0, 255));
            using Mat dummy2 = new Mat(320, 320, MatType.CV_8UC3, new Scalar(0, 0, 255));

            var images = new List<Mat>
            {
                dummy1,
                dummy2
            };
            var res = ClassifyObjects(images);
            _logger.LogInformation("Detector initalized");
        }

        private const double scaleFactor = 1.0 / 255;
        private readonly Size scaleSize = new Size(320, 320);

        public IList<DnnDetectedObject[]> ClassifyObjects(IList<Mat> images)
        {
            if (images is null) throw new ArgumentNullException(nameof(images));

            foreach (var image in images)
            {
                if (image?.Empty() == true) throw new ArgumentNullException(nameof(images), "One of the images is not initialized");
            }

            using var blob = CvDnn.BlobFromImages(images, scaleFactor, scaleSize, crop: false);
            nnet.SetInput(blob);

            //forward model
            nnet.Forward(outs, _outNames);

            if (images.Count == 1)
            {
                return ExtractYoloSingleResults(outs, images[0], threshold, nmsThreshold);
            }
            else
            {
                return ExtractYoloBatchedResults(outs, images, threshold, nmsThreshold);
            }
        }

        private readonly List<int> _classIds = new List<int>();
        private readonly List<float> _confidences = new List<float>();
        private readonly List<float> _probabilities = new List<float>();
        private readonly List<Rect2d> _boxes = new List<Rect2d>();


        private IList<DnnDetectedObject[]> ExtractYoloBatchedResults(IEnumerable<Mat> output, IEnumerable<Mat> image, float threshold, float nmsThreshold, bool nms = true)
        {
            var inputImages = image.ToList();

            DnnDetectedObject[][] results = new DnnDetectedObject[inputImages.Count][];


            for (int inputIndex = 0; inputIndex < inputImages.Count; inputIndex++)
            {
                //for nms
                _classIds.Clear();
                _confidences.Clear();
                _probabilities.Clear();
                _boxes.Clear();

                var w = inputImages[inputIndex].Width;
                var h = inputImages[inputIndex].Height;
                /*
                 YOLO3 COCO trainval output
                 0 1 : center                    2 3 : w/h
                 4 : confidence                  5 ~ 84 : class probability 
                */
                const int prefix = 5;   //skip 0~4

                foreach (var prob in output)
                {
                    //dimensions will be 2 for single image analysis and 3 for batch analysis
                    //2 input Prob Dims:3 images: 0 - 2, 1 - 300, 2 - 85

                    var probabilitiesRange = new Range(prefix, prob.Size(2) - 1);

                    for (var i = 0; i < prob.Size(1); i++)
                    {
                        var confidence = prob.At<float>(inputIndex, i, 4);

                        //Filter out bogus results of > 100% confidence
                        if (confidence > threshold && confidence <= 1.0)
                        {
                            var maxProbIndex = prob.FindMaxValueIndexInRange<float>(inputIndex, i, probabilitiesRange);

                            if (maxProbIndex == -1)
                            {
                                continue;
                            }

                            var probability = prob.At<float>(inputIndex, i, maxProbIndex);

                            if (probability > threshold) //more accuracy, you can cancel it
                            {
                                //get center and width/height
                                var centerX = prob.At<float>(inputIndex, i, 0) * w;
                                var centerY = prob.At<float>(inputIndex, i, 1) * h;
                                var width = prob.At<float>(inputIndex, i, 2) * w;
                                var height = prob.At<float>(inputIndex, i, 3) * h;

                                float X = Math.Max(0, centerX - (width / 2.0f));
                                float Y = Math.Max(0, centerY - (height / 2.0f));

                                //put data to list for NMSBoxes
                                _classIds.Add(maxProbIndex - prefix);
                                _confidences.Add(confidence);
                                _probabilities.Add(probability);
                                _boxes.Add(new Rect2d(X, Y, width, height));
                            }
                        }
                    }
                }

                results[inputIndex] = OptimizeDetections(threshold, nmsThreshold, nms).ToArray();
            }

            return results;
        }

        private IList<DnnDetectedObject[]> ExtractYoloSingleResults(IEnumerable<Mat> output, Mat image, float threshold, float nmsThreshold, bool nms = true)
        {
            DnnDetectedObject[][] results = new DnnDetectedObject[1][];

            //for nms
            _classIds.Clear();
            _confidences.Clear();
            _probabilities.Clear();
            _boxes.Clear();

            var w = image.Width;
            var h = image.Height;
            /*
             YOLO3 COCO trainval output
             0 1 : center                    2 3 : w/h
             4 : confidence                  5 ~ 84 : class probability 
            */
            const int prefix = 5;   //skip 0~4

            foreach (var prob in output)
            {
                //dimensions will be 2 for single image analysis and 3 for batch analysis
                //1 input Prob Dims:2 images: 0 - 300, 1 - 85

                for (var i = 0; i < prob.Size(0); i++)
                {
                    var confidence = prob.At<float>(i, 4);

                    //Filter out bogus results of > 100% confidence
                    if (confidence > threshold && confidence <= 1.0)
                    {
                        var colRange = new Range(prefix, prob.Size(1) - 1);

                        var maxProbIndex = prob.FindMaxValueIndexInRange<float>(i, colRange);

                        if (maxProbIndex == -1)
                        {
                            continue;
                        }

                        var probability = prob.At<float>(i, maxProbIndex);

                        if (probability > threshold) //more accuracy, you can cancel it
                        {
                            //get center and width/height
                            var centerX = prob.At<float>(i, 0) * w;
                            var centerY = prob.At<float>(i, 1) * h;
                            var width = prob.At<float>(i, 2) * w;
                            var height = prob.At<float>(i, 3) * h;

                            float X = Math.Max(0, centerX - (width / 2.0f));
                            float Y = Math.Max(0, centerY - (height / 2.0f));

                            //put data to list for NMSBoxes
                            _classIds.Add(maxProbIndex - prefix);
                            _confidences.Add(confidence);
                            _probabilities.Add(probability);
                            _boxes.Add(new Rect2d(X, Y, width, height));
                        }
                    }
                }
            }

            results[0] = OptimizeDetections(threshold, nmsThreshold, nms).ToArray();

            return results;
        }

        private List<DnnDetectedObject> OptimizeDetections(float threshold, float nmsThreshold, bool nms)
        {
            int[] indices;

            if (!nms)
            {
                indices = Enumerable.Range(0, _boxes.Count).ToArray();
            }
            else
            {
                //using non-maximum suppression to reduce overlapping low confidence box
                CvDnn.NMSBoxes(_boxes, _confidences, threshold, nmsThreshold, out indices);
                _logger.LogDebug("NMSBoxes drop {overlappingResults} overlapping result.",
                    _confidences.Count - indices.Length);
            }

            var result = new List<DnnDetectedObject>(indices.Length);

            foreach (var i in indices)
            {
                var box = _boxes[i];
                var classIndex = _classIds[i];

                var detection = new DnnDetectedObject()
                {
                    Index = _classIds[i],
                    Label = Labels[classIndex],
                    Color = Colors[classIndex],
                    Probability = _probabilities[i],
                    BoundingBox = box
                };
                result.Add(detection);
            }

            return result;
        }

        public void Dispose()
        {
            if (nnet.IsEnabledDispose && !nnet.IsDisposed)
            {
                nnet.Dispose();
            }
        }
    }
}
