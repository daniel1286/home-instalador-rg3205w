#!/usr/bin/env bash
# Lanzador del SNI TLS (:443). Lee /etc/default/mh-fake y arma los argumentos
# segun el modo (onboarding -> --no-update; ota -> ofrece OTA).
set -eu
[ -f /etc/default/mh-fake ] && . /etc/default/mh-fake
MH_MODE=${MH_MODE:-onboarding}
MH_IP=${MH_IP:-10.42.0.1}
MH_PIVOT_URL=${MH_PIVOT_URL:-http://10.42.0.1:8000/pivot_ota_rg3205w_home.zip}
MH_PIVOT_SIZE=${MH_PIVOT_SIZE:-33844257}
PIVOT_FILE=$(basename "$MH_PIVOT_URL")

EXTRA=""
if [ "$MH_MODE" = "onboarding" ]; then
    EXTRA="--no-update"
fi

exec /usr/bin/python3 /opt/mh-fake/sni_server.py \
    --bind 0.0.0.0:443 \
    --certs /opt/mh-fake/certs \
    --ota-root "http://${MH_IP}:8000" \
    --pivot-file "$PIVOT_FILE" \
    --pivot-size "$MH_PIVOT_SIZE" \
    --video-dir /opt/mh-fake/video \
    $EXTRA
