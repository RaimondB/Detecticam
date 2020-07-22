#!/bin/bash

sleep 5

FILE=""
DIR="/captures"
# init
# look for empty dir 
if [ "$(ls -A $DIR)" ]; then
    exit 0
else
    exit 1
fi
# rest of the logic