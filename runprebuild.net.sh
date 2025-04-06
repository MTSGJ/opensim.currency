#!/bin/bash
#
DOTNETVER=9.0

if [ "$1" != "" ]; then
    DOTNETVER=$1
fi
VER=`echo $DOTNETVER | sed -e "s/\./_/"`

echo y | dotnet ../bin/prebuild.dll /file prebuild.net.xml /clean
dotnet ../bin/prebuild.dll /target vs2022 /targetframework net${VER} /excludedir = "obj | bin" /file prebuild.net.xml

