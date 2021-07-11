#!/bin/bash

echo "=== Start Test Run ==="
./DetectiCam --configdir=/config &
PROC=$!
sleep 5

echo "=== Check Snapshot ==="
curl -o /tmp/snapshot.png -f http://localhost/api/camera/test-video/snapshot
SNAPSHOT=$?

if [ $SNAPSHOT -eq 0 ]
then
  echo "== Check SnapShot: OK =="
else
  echo "== Check SnapShot: Fail ==" >&2
  exit 1
fi

wait $PROC

FILE=""
DIR="/captures"
# init
# look for empty dir 
if [ "$(ls -A $DIR)" ]; then
    echo "=== Check Captures : found ==="
    exit 0
else
    echo "=== Check Captures : not found ===" >&2
    exit 1
fi
# rest of the logic