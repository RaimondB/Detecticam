# Detect-i-cam

Detect-i-cam is a solution for monitoring camera's with A.I. object detection by using the Yolo Convolutional Neural Network

This solution offers the following features:

* Docker image runnable on linux
* Able to monitor multiple camera streams in parallel
* Batch processing the captured images in the CNN for efficiency
* Using OpenCV
* Webhook notification
* Saving annotated captured frames to check on detections
* MQTT notification
* Ability to filter on configurable objects from all of the 80 yolo3 classes
* Configurable Region-Of-Interest (ROI) to constrain detections to a specific area
* Downloading a snapshot via REST API for a configured camera

## Getting Started

Your can run Detect-i-cam as a docker container or as a commandline tool. Docker is advised.

## Using Detect-i-cam with docker

* Pull the linux based image

```bash
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

This is configured in the [appsettings.json](https://github.com/RaimondB/Detecticam/blob/master/DetectiCam/appsettings.template.json) file

The minimum config needed is to have the *video-stream* section.
Only the id and path are required. A path can be a videostream file (need to be reachable via mapped volume), a http url to an IP Cam or a rtsp stream.

```json
{
  "video-streams": [
    {
      "id": "side-door-live",
      "path": "rtsp://<user>:<passwd>@<cameraip>:<port>/<part>",
      "rotate": "Rotate90Clockwise",
      "fps": 15,
      "callbackUrl": "http:\/\/nas.home:5000\/webapi\/entry.cgi?api=SYNO.SurveillanceStation.Webhook&method=\"Incoming\"&version=1&token=x",
      "ROI": { "Left": 0, "Top": 172, "Right" : 479, "Bottom" : 639 },
      "additionalObjectWhitelist": [ "cat" ]
    }
  ]
}
```

* *id*: Mandatory, unique id identifying the camera stream
* *path*: Mandatory file, http or rstp stream. Please check the docs of you IP-cam on the correct format.
* *rotate*: Optional, when left out no rotation happens. Can be usefull if your cam does not support rotation natively.
* *fps*: Optional, only needed if the log shows that the settings cannot be picked up from the stream. If nothing can be found, and not specified, defaults to 30.
* *ROI*: Optional, used to restrict the detection area (region of interest) on the video (after rotation)
* *callbackUrl*: Optional, webhook to trigger on detection
* *additionalObjectWhitelist*: Optional, additional classes to detect specific on this videostream on top of the global list.

## Configure detections

The objects to detect can be configured to a subset of the classes offered by the yolo network.

```json
{
  "detection": {
    "detectionThreshold": 0.5,
    "objectWhitelist": [ "person", "car" ]
}
```

* *detectionThreshold*: Optional, default 0.5, minimum probability to keep a detection of the network. You can tune the level to fit your use-case and prevent false detections.
* *objectWhitelist*: Optional, when not specified it will detect only the *person* class.

## Act on detections

### Using Tokens

For the *capturePattern* and the *callbackUrl* it is possible to use tokens to replace parts to make it dynamic based on the current stream and detections.

There are a few special tokens:

* *{streamId}* is the id of the videostream
* *{ts}* is the sortable timestamp
* *{dobj}* is a comma-separated list of the labels of the detectedobjects

Additionally, you can provide [all normal timestamp formatters](https://docs.microsoft.com/en-us/dotnet/standard/base-types/custom-date-and-time-format-strings) between {} .

### Capturing Detections to disk

By default, all captured images containing a "person" are written to the /captures volume, including bounding boxes of all detected objects and their confidence percentage.
This can be configured by the following settings in the appsettings.json:

```json
"capture-publisher": {
    "enabled": true,
    "captureRootDir" : "/captures",
    "capturePattern" : "{yyyy-MM-dd}/{streamId}-{ts}.jpg"
}
```

it is now possible to configure the way the captures will be saved by using Tokens.
This way it is also now possible to dynamically generate subdirectories so that it is easy to group files by date.

The previous setting for this as shown below is now deprecated and will be removed in a future release.

```json
"capture-path": "./captures",
```

### Webhook Notification

When you want to integrate with other solutions by using a webhook, you can specifiy a callback url. As part of the video-steam config. 
In the example I am using a webhook to trigger recording on my synology NAS.

### MQTT Publications

Another option is to use MQTT publication. The following configuration can be used in the appsettings.json:

```json
"mqtt-publisher": {
   "enabled": true,
   "server": "nuc.home",
   "port": 1883,
   "username": "myuser",
   "password": "passwd",
   "topicPrefix": "home",
   "clientId": "detecticam",
   "includeDetectedObjects": false,
   "topDetectedObjectsLimit" : 3
 }
```

* *enabled*: Mandatory to enable it, by default is set to false.
* *server*: Mandatory, Mqtt server to use
* *port*: Optional, default set to 1883.
* *username*: Optional, default null.
* *password*: Optional, default null.
* *topicPrefix*: Optional, default ""
* *clientId*: Optional, by default a unique id (guid) is generated
* *includeDetectedObjects*: Optional, default false: data of detectedobjects is not included
* *topDetectedObjectsLimit*: Optional, default 3: will only report the 3 objects with the top probability to limit the amount of data.

Currently only an unsecure connection is supported, so we don't have to configure certificates etc.

A message will be published each time a person is detected.
The message is published on the topic:

```bnf
[<topicPrefix>/]detect-i-cam/<stream-id>/state
```

The value of the message (when not including detected objects) is:

```json
{ "detection" : true }
```

When includeDetectedObjects:true, then the message looks as follows:

```json
{ "detection":true,
  "detectedObjects":
  [
    {
      "Index":2,
     "Label":"car",
     "Probability":0.99284625,
     "BoundingBox":{"X":0,"Y":62,"Width":120,"Height":73}
    }
  ]
}
```

*topDetectedObjectsLimit* determines the amount of objects thta will be reported, sorted descending on the *probability*.

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

## Creating snapshots

Aside from detection, it is often usefull to have a fast way to create a camera snapshot. Since detect-i-cam is already continuously grabbing frames, I decided to add this functionality. In this case to have a fast snapshot mechanism for when somebody presses the doorbell.

As there is no authentication built-in, this feature should only be enabled on a secure network. Therefore the feature is switched off by default. I can be enabled as shown below.

```json
{
  "snapshot": {
    "enabled": true
  }
}
```

To get a snapshot, simply execute a GET request towards the following path:

```web
http://<host>:<port>/api/camera/<stream-id>/snapshot
```

This will return a PNG image. To get access to the port, map port 80 of the container to an appropriate port on your Docker host.

## Contributing

We welcome contributions. Feel free to file issues and pull requests on the repo and we'll address them as we can. Learn more about how you can help on our [Contribution Rules & Guidelines](CONTRIBUTING.md).

## License

Detect-i-cam licensed with the GPLv3 License. For more details, see [LICENSE](LICENSE.md).
