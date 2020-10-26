using DetectiCam.Core.Common;
using DetectiCam.Core.VideoCapturing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Range = OpenCvSharp.Range;

namespace DetectiCam.Core.Detection
{
    public sealed class YoloBatchedDnnDetector : ConfigurableService<YoloBatchedDnnDetector, Yolo3Options>, IBatchedDnnDetector
    {
        private readonly Scalar[] Colors;
        private readonly string[] Labels;
        private readonly OpenCvSharp.Dnn.Net nnet;
        private readonly Mat[] outs;
        private readonly string[] _outNames;
        private readonly Dictionary<string, Region?> _roiConfig;

        private const float nmsThreshold = 0.3f;    //threshold for nms

        public YoloBatchedDnnDetector(ILogger<YoloBatchedDnnDetector> logger, IOptions<Yolo3Options> yoloOptions, IOptions<VideoStreamsOptions> streamOptions) :
            base(logger, yoloOptions)
        {
            _roiConfig = GetValidatedOptions<VideoStreamsOptions>(streamOptions).
                ToDictionary(o => o.Id, o => o.ROI);

            //YOLOv3 Fiel locations
            //Cfg: https://github.com/pjreddie/darknet/blob/master/cfg/yolov3.cfg
            //Weight: https://pjreddie.com/media/files/yolov3.weights
            //Names: https://github.com/pjreddie/darknet/blob/master/data/coco.names

            var cfg = Path.Combine(Options.RootPath, Options.ConfigFile);
            var weight = Path.Combine(Options.RootPath, Options.WeightsFile);
            var names = Path.Combine(Options.RootPath, Options.NamesFile);

            //random assign color to each label
            Labels = File.ReadAllLines(names).ToArray();

            //get labels from coco.names
            Colors = Enumerable.Repeat(false, Labels.Length).Select(x => Scalar.RandomColor()).ToArray();

            Logger.LogInformation("Loading Neural Net");
            nnet = OpenCvSharp.Dnn.CvDnn.ReadNetFromDarknet(cfg, weight);

            _outNames = nnet.GetUnconnectedOutLayersNames()!;

            outs = Enumerable.Repeat(false, _outNames.Length).Select(_ => new Mat()).ToArray();
        }

        public void Initialize()
        {
            Logger.LogInformation("Start Detector initalize & warmup");
            using Mat dummy1 = new Mat(320, 320, MatType.CV_8UC3, new Scalar(0, 0, 255));
            using Mat dummy2 = new Mat(320, 320, MatType.CV_8UC3, new Scalar(0, 0, 255));

            var images = new List<Mat>
            {
                dummy1,
                dummy2
            };
            InternalClassifyObjects(images, 0.5f);
            Logger.LogInformation("Detector initalized");
        }

        private const double scaleFactor = 1.0 / 255;
        private readonly Size scaleSize = new Size(320, 320);

        public IList<DnnDetectedObject[]> ClassifyObjects(IList<VideoFrame> frames, float detectionThreshold)
        {
            if (frames is null) throw new ArgumentNullException(nameof(frames));
            
            //Crop frame to ROI if speficied
            var imageInfos = frames.Where(f => f.Image != null).Select(f =>
            {
                var roi = _roiConfig.GetValueOrDefault(f.Metadata.Info.Id, null);
                if (roi != null)
                {
                    Logger.LogDebug("Cropping for ROI: {vsid}", f.Metadata.Info.Id);
                    int topC = roi.Top.CropInRange(0, f.Image.Rows - 1);
                    int bottomC = roi.Bottom.CropInRange(0, f.Image.Rows - 1);
                    int leftC = roi.Left.CropInRange(0, f.Image.Cols - 1);
                    int rightC = roi.Right.CropInRange(0, f.Image.Cols - 1);
                    
                    var roiC = new Region() { Top = topC, Bottom = bottomC, Left = leftC, Right = rightC };

                    if(topC != roi.Top || bottomC != roi.Bottom || leftC != roi.Left || rightC != roi.Right)
                    {
                        Logger.LogWarning("The configured ROI does not fit within the frame. Adjusted from {roi} to {roiC}", roi, roiC);
                    }
                    roi = roiC;
                }

                return new
                {
                    Image = roi == null ? f.Image : f.Image[new Range(roi.Top, roi.Bottom), new Range(roi.Left, roi.Right)],
                    ROI = roi
                };

            }).ToList();

            //Execute the core detection
            var detectedObjects = InternalClassifyObjects(imageInfos.Select(f => f.Image).ToList(), detectionThreshold);

            //Correct detected objects location based on ROI (shift relative to topleft of ROI)
            var correctedObjects = detectedObjects.Zip(imageInfos, (dobjs, inf) =>
                inf.ROI == null ? dobjs : dobjs.Select(dobj => {
                    var bb = dobj.BoundingBox;
                    dobj.BoundingBox = new Rect2d(bb.X + inf.ROI.Left, bb.Y + inf.ROI.Top, bb.Width, bb.Height);
                    return dobj;
                }).ToArray()                                                                                                      
            ).ToList();

            //Cleanup the cropped images, since they are no longer needed
            foreach (var croppedImage in imageInfos.Where(io => io.ROI != null).Select(io => io.Image))
            {
                croppedImage.SafeDispose();
            }

            return correctedObjects;
        }


        private IList<DnnDetectedObject[]> InternalClassifyObjects(IList<Mat> images, float detectionThreshold)
        { 
            using var blob = CvDnn.BlobFromImages(images, scaleFactor, scaleSize, crop: false);
            nnet.SetInput(blob);

            //forward model
            nnet.Forward(outs, _outNames);

            if (images.Count == 1)
            {
                return ExtractYoloSingleResults(outs, images[0], detectionThreshold, nmsThreshold);
            }
            else
            {
                return ExtractYoloBatchedResults(outs, images, detectionThreshold, nmsThreshold);
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
                Logger.LogDebug("NMSBoxes drop {overlappingResults} overlapping result.",
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
