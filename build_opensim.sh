#!/bin/bash

CONFIGPATH=./config
OPNSIMPATH=../bin


echo "=============================================="
echo "Rebuild OpenSim for Secure DTL/NSL_CURRENCY"
echo "=============================================="


function xbuild_opensim()
{
	./runprebuild.sh vs2010
	xbuild /target:CLean
	xbuild || return 1

	cd opensim.currency
	./build.sh
    cd ..

	if [ -d opensim.modules ]; then
    	cd opensim.modules 
		./build.sh || return 1
		cd ..
	fi
}




if [ "$1" != '-R' ]; then
	#
	if [ ! -f ../bin/HttpServer_OpenSim.dll.orig ]; then
		mv ../bin/HttpServer_OpenSim.dll ../bin/HttpServer_OpenSim.dll.orig
	fi
	cp -f HttpServer_OpenSim/bin/HttpServer_OpenSim.dll ../bin

	cd ..

	PCHK=`grep "request.IHttpClientContext.SSLCommonName" OpenSim/Framework/Servers/HttpServer/BaseHttpServer.cs`

	if [ "$PCHK" = "" ]; then
		patch -p1 < ./opensim.currency/patch/opensim.patch || exit 1
	fi
	xbuild_opensim || exit 1
	#
else
	#
	if [ -f ../bin/HttpServer_OpenSim.dll.orig ]; then
		rm -f ../bin/HttpServer_OpenSim.dll
		mv ../bin/HttpServer_OpenSim.dll.orig ../bin/HttpServer_OpenSim.dll
	fi

	cd ..
	patch -p1 -R < ./opensim.currency/patch/opensim.patch || exit 1
	xbuild_opensim || exit 1
	#
fi

