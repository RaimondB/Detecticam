docker stop yolov3
docker rm yolov3
docker run --name yolov3 -d raimondb/yolov3-data tail -f /dev/null
mkdir ..\YoloData
docker cp yolov3:coco.names ../YoloData/
docker cp yolov3:yolov3.weights ../YoloData/
docker cp yolov3:yolov3.cfg ../YoloData/
docker stop yolov3
docker rm yolov3
REM --target base -t detect-i-cam:latest ..