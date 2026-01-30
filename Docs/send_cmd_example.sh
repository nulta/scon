#!/bin/bash

# Docker attach:
# echo $COMMAND | socat EXEC:"docker attach 71e3309f-e046-4aa2-a348-574c5f6f90fa",pty STDIN

curl -X POST "https://example.org/api/console" \
    -H "Authorization: Bearer EXAMPLE_TOKEN" \
    -H "Content-Type: application/json" \
    -d "{\"command\": \"$( printf $* | base64 -d | sed 's/"/\\"/g' )\"}"
