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

DT=`date`

if ! nc -z -w $TIMEOUT $HOST $PORT; then
    echo "Attention: Money Server Https Port is stopped: "$DT >> $LOGFL
    /usr/bin/tmux send-keys -t opensim_money C-m "quit" C-m
    sleep 5
    systemctl start opensim_money.net.service
    exit 1
else
    echo "Money Server Https Port is checked: "$DT >> $LOGFL
fi
