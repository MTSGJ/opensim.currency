#!/bin/bash
#
# Usage ... ./build.net.sh [dotnet_version]
#

DOTNETVER=8.0

CONFIGPATH=./config
OPNSIMPATH=../bin

echo "===================="
echo "  DTL/NSL_CURRENCY"
echo "===================="

if [ "$1" != "" ]; then
    DOTNETVER=$1
fi

./clean.sh
./runprebuild.net.sh $DOTNETVER
dotnet build -c Release OpenSim.Currency.sln || exit 1

echo
cp -f bin/net${DOTNETVER}/OpenSim.Data.MySQL.MySQLMoneyDataWrapper.dll $OPNSIMPATH
cp -f bin/net${DOTNETVER}/OpenSim.Modules.Currency.dll $OPNSIMPATH
cp -f bin/net${DOTNETVER}/MoneyServer.dll $OPNSIMPATH
cp -f bin/net${DOTNETVER}/MoneyServer $OPNSIMPATH
cp -f bin/net${DOTNETVER}/MoneyServer.runtimeconfig.json $OPNSIMPATH

#
rm -f $OPNSIMPATH/OpenSim.Forge.Currency.dll

if [ ! -f $OPNSIMPATH/MoneyServer.ini ]; then
	cp $CONFIGPATH/MoneyServer.ini $OPNSIMPATH
else
	cp $CONFIGPATH/MoneyServer.ini $OPNSIMPATH/MoneyServer.ini.example
fi

if [ ! -f $OPNSIMPATH/MoneyServer.dll.config ]; then
	cp $CONFIGPATH/MoneyServer.exe.config $OPNSIMPATH/MoneyServer.dll.config
fi

# Sample Server Cert file 1 for MoneyServer.exe
if [ ! -f $OPNSIMPATH/SineWaveCert.pfx ]; then
	cp $CONFIGPATH/SineWaveCert.pfx $OPNSIMPATH
fi

# Sample Server Cert file 2 (JOGRID.NET) for MoneyServer.exe. No password
if [ ! -f $OPNSIMPATH/server_cert.p12 ]; then
	cp $CONFIGPATH/server_cert.p12 $OPNSIMPATH
fi


