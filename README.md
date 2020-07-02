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
* Pull the image located at ...
* Linux based image
* Run it directly, or use [docker-compose](./docker-example/docker-compose.yml)
* Provide your config file as indicated below in the *config volume*

## Using Detect-i-cam as a CommandLine application
* Provide an appsettings.json file configured as indication below.
* By default this is expected in the same directory as Detect-i-cam
* The location can be overridden by specifying the --configdir option

## Configure your streams
In order to run Detect-i-cam, you must provide the videstreams to be monitored.

This is configured in the [appsettings.json](./docker-example/config/appsettings.json) file
See the file for the options and their meaning.




## Contributing

We welcome contributions. Feel free to file issues and pull requests on the repo and we'll address them as we can. Learn more about how you can help on our [Contribution Rules & Guidelines](CONTRIBUTING.md). 

## License

Detect-i-cam licensed with the GPLv3 License. For more details, see [LICENSE](LICENSE.md).


