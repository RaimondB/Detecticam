# Save current working directory
cwd=$(pwd)

echo 'debconf debconf/frontend select Noninteractive' | debconf-set-selections

apt-get -y update
apt-get -y upgrade

apt-get -y install apt-utils

apt-get -y install	libgtk2.0-0 \
					libtesseract4 \
					libdc1394-22 \
					libavformat57 \
					libswscale4 \
					libopenexr22

#apt-get -y install libgtk2.0-0
#apt-get -y install libtesseract4 #6941 KB
#apt-get -y install libdc1394-22 #460 kB
#apt-get -y install libavformat57 #185 MB
#apt-get -y install libswscale4 #622 kB
#apt-get -y install libopenexr22 #3646 kB

apt-get -y autoremove
apt-get clean