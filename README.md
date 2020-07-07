# Detect-i-cam

Detect-i-cam is a solution for monitoring camera's with A.I. object detection by using the Yolo Convolutional Neural Network

This solution offers the following features:
* Docker image runnable on linux
* Able to monitor multiple camera streams in parallel
* Batch processing the captured images in the CNN for efficiency
* Using OpenCV, supporting GPU acceleration
* Webhook notification
* Saving annotated captured frames to check on detections


## Getting Started
Your can run Detect-i-cam as a docker container or as a commandline tool. Docker is advised.

## Using Detect-i-cam with docker
* Pull the linux based image
```
docker pull raimondb/detect-i-cam
```

* Run it directly, or use docker-compose as in the example below
* Provide your appsettings.json config file as indicated below in the *config volume*

```yaml
---
version: "2.2"
services:
  detect-i-cam:
    image: raimondb/detect-i-cam
    container_name: detect-i-cam
    volumes:
      - ./capture:/captures
      - ./config:/config
    restart: unless-stopped
```


## Using Detect-i-cam as a CommandLine application
* Provide an appsettings.json file configured as indication below.
* By default this is expected in the same directory as Detect-i-cam
* The location can be overridden by specifying the --configdir option

## Configure your streams
In order to run Detect-i-cam, you must provide the videstreams to be monitored.

This is configured in the [appsettings.json](./docker-example/config/appsettings.json) file

The minimum config needed is shown below.
Only the id and path are required. A path can be a videostream file (need to be reachable via mapped volume), a http url to an IP Cam or a rtsp stream.

```json
{
  "video-streams": [
    {
      "id": "side-door-live",
      "path": "rtsp://<user>:<passwd>@<cameraip>:<port>/<part>",
      "rotate": "Rotate90Clockwise",
      "fps": 15,
      "callbackUrl": "http:\/\/nas.home:5000\/webapi\/entry.cgi?api=SYNO.SurveillanceStation.Webhook&method=\"Incoming\"&version=1&token=x"
    }
  ]
}
```
* *id*: Mandatory, unique id identifying the camera stream
* *path*: Mandatory file, http or rstp stream. Please check the docs of you IP-cam on the correct format.
* *rotate*: Optional, when left out no rotation happens. Can be usefull if your cam does not support rotation natively.
* *fps*: Optional, only needed if the log shows that the settings cannot be picked up from the stream. If nothing can be found, and not specified, defaults to 30.
* *callbackUr*l: Optional, webhook to trigger on detection

### Captures or Callback
By default, all captured images containing a "person" are written to the /captures volume, including bounding boxes of all detected objects and their confidence percentage.
When you want to integrate with other solutions, you can specifiy a callback url. In the example I am using a webhook to trigger recording on my synology NAS.

### Rolling your own network

You might also want to use different Yolov3 Darknet compatible configuration.
These data files can be provide via the /config volume mapped from the image. By default, the image already contains the full YoloV3 network.
So you only need to change this if you have e.g. trained your own dataset. Or maybe if you want to use YoloV3 tiny to save on memory usage and CPU.
The below "yolov3" section of configuration should than be added to your appsettings.json config file.

```json
{
  "yolo3": {
     "rootPath": "/config/yolo-data",
     "namesFile": "coco.names",
     "configFile": "yolov3.cfg",
     "weightsFile": "yolov3.weights"
   }
}
```

* *rootPath*: Optional, rootpath to load the three files below from. Located in a directory in the /config volume normally
* *namesFile*: Optional, Contains are the recognized labels in Coco Format
* *configFile*: Optional, Darknet Config file
* *weightsFile*: Optional, Darknet Weights file.


## Contributing

We welcome contributions. Feel free to file issues and pull requests on the repo and we'll address them as we can. Learn more about how you can help on our [Contribution Rules & Guidelines](CONTRIBUTING.md). 

## License

Detect-i-cam licensed with the GPLv3 License. For more details, see [LICENSE](LICENSE.md).
