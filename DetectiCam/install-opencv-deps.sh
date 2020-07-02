# Save current working directory
cwd=$(pwd)

echo 'debconf debconf/frontend select Noninteractive' | debconf-set-selections

 apt-get -y update
 apt-get -y upgrade

 apt-get -y install apt-utils

 apt-get -y remove x264 libx264-dev

## Install dependencies
# apt-get -y install build-essential checkinstall cmake pkg-config yasm
# apt-get -y install git gfortran
apt-get -y install libjpeg8-dev libpng-dev
#apt-get -y install libjpeg62-turbo-dev libpng-dev

# apt-get -y install software-properties-common
 add-apt-repository "deb http://security.ubuntu.com/ubuntu xenial-security main"
 apt-get -y update

 apt-get -y install libjasper1
 apt-get -y install libtiff-dev

 apt-get -y install libavcodec-dev libavformat-dev libswscale-dev libdc1394-22-dev
 apt-get -y install libxine2-dev libv4l-dev
cd /usr/include/linux
 ln -s -f ../libv4l1-videodev.h videodev.h
cd "$cwd"

 apt-get -y install libgstreamer1.0-dev libgstreamer-plugins-base1.0-dev
 apt-get -y install libgtk2.0-dev libtbb-dev qt5-default
 apt-get -y install libatlas-base-dev
 apt-get -y install libfaac-dev libmp3lame-dev libtheora-dev
# apt-get -y install libvorbis-dev libxvidcore-dev
 apt-get -y install libopencore-amrnb-dev libopencore-amrwb-dev
 apt-get -y install libavresample-dev
 apt-get -y install x264 v4l-utils

 apt-get -y install tesseract-ocr
 apt-get -y install libopenexr22

# Optional dependencies
# apt-get -y install libprotobuf-dev protobuf-compiler
# apt-get -y install libgoogle-glog-dev libgflags-dev
# apt-get -y install libgphoto2-dev libeigen3-dev libhdf5-dev doxygen