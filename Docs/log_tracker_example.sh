#!/bin/bash

# Log file directory
# Consider using bindfs(8) if necessary to adjust permissions
LOGS_DIR=/home/scon/ulx_logs
cd $LOGS_DIR || exit 1

while true; do
    CURRENT_DATE=$(/usr/bin/date +%Y-%m-%d)
    LOG_FILE="${CURRENT_DATE}.txt"

    NEXT_DAY_SECONDS=$(/usr/bin/date -d "tomorrow 00:00:01" +%s)
    NOW_SECONDS=$(/usr/bin/date +%s)
    SECONDS_LEFT=$((NEXT_DAY_SECONDS - NOW_SECONDS))

    # Use timeout to limit tail duration to the remaining seconds in the day
    /usr/bin/timeout $SECONDS_LEFT /usr/bin/tail -n 50 -F "$LOG_FILE"
done
