#!/bin/bash
#
DOTNETVER=8

if [ "$1" != "" ]; then
    DOTNETVER=$1
fi

echo y | dotnet ../bin/prebuild.dll /file prebuild.net.xml /clean

dotnet ../bin/prebuild.dll /target vs2022 /targetframework net${DOTNETVER}_0 /excludedir = "obj | bin" /file prebuild.net.xml

