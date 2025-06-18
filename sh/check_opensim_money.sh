#!/bin/bash

# OpenSimulator Check Money Server Script
#                                           v0.1 20250523
#
# crontab
# */5 * * * * /usr/local/bin/check_opensim_money.sh >/dev/null 2>&1
#

HOST="localhost"
PORT=8008
TIMEOUT=3  # タイムアウト秒数
LOGFL="/var/log/openim_money_stop.log"

#
if [ ! -f LOGFL ]; then
    touch $LOGFL
fi

DT=`date +%Y-%m-%d" "%T`

if ! nc -z -w $TIMEOUT $HOST $PORT; then
    echo $DT" ERROR - Money Server Https Port is stopped." >> $LOGFL
    systemctl restart opensim_money.net.service
    exit 1
else
    echo $DT" INFO  - Money Server Https Port is checked." >> $LOGFL
fi
