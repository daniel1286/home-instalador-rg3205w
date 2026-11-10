#!/usr/bin/env bash
# Lanzador del SNI TLS (:443). Lee /etc/default/mh-fake y arma los argumentos
# segun el modo (onboarding -> --no-update; ota -> ofrece OTA).
set -eu
[ -f /etc/default/mh-fake ] && . /etc/default/mh-fake
MH_MODE=${MH_MODE:-onboarding}
MH_IP=${MH_IP:-10.42.0.1}
MH_PIVOT_URL=${MH_PIVOT_URL:-http://10.42.0.1:8000/pivot_ota_rg3205w_home.zip}
MH_PIVOT_SIZE=${MH_PIVOT_SIZE:-1128435}
PIVOT_FILE=$(basename "$MH_PIVOT_URL")

# El tamaño anunciado en /v1/updates/fw debe coincidir con el fichero servido.
# Si el pivot ya está descargado, usa su tamaño REAL en disco (evita desajustes
# al re-subir el OTA sin actualizar MH_PIVOT_SIZE).
OTA_DIR=/opt/mh-fake/otas
[ -f "$OTA_DIR/$PIVOT_FILE" ] && MH_PIVOT_SIZE=$(stat -c%s "$OTA_DIR/$PIVOT_FILE")

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
