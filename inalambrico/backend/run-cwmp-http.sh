#!/usr/bin/env bash
# Lanzador del ACS CWMP/TR-069 en HTTP plano (:7006). El usr.cfg de fabrica
# apunta ManagementServer.URL a http://main.acs.telefonica.net:7006, asi que
# este es el puerto que el aparato usa por defecto. Lee /etc/default/mh-fake.
set -eu
[ -f /etc/default/mh-fake ] && . /etc/default/mh-fake
MH_PIVOT_URL=${MH_PIVOT_URL:-http://10.42.0.1:8000/pivot_ota_rg3205w_home.zip}
MH_PIVOT_SIZE=${MH_PIVOT_SIZE:-1128435}
MH_HOME_URL=${MH_HOME_URL:-http://10.42.0.1:8000/Home-ota-rg3205w.zip}
MH_HOME_SIZE=${MH_HOME_SIZE:-1108418366}
MH_ACS_AUTH=${MH_ACS_AUTH:-}

# El FileSize del Download RPC debe coincidir con el fichero, o el CPE rechaza
# la OTA. Si los OTA ya están descargados, usa sus tamaños REALES en disco.
OTA_DIR=/opt/mh-fake/otas
[ -f "$OTA_DIR/$(basename "$MH_PIVOT_URL")" ] && MH_PIVOT_SIZE=$(stat -c%s "$OTA_DIR/$(basename "$MH_PIVOT_URL")")
[ -f "$OTA_DIR/$(basename "$MH_HOME_URL")" ]  && MH_HOME_SIZE=$(stat -c%s "$OTA_DIR/$(basename "$MH_HOME_URL")")

exec /usr/bin/python3 /opt/mh-fake/cwmp_server.py \
    --bind 0.0.0.0:7006 --no-tls \
    --auth "$MH_ACS_AUTH" \
    --pivot-url "$MH_PIVOT_URL" --pivot-size "$MH_PIVOT_SIZE" \
    --home-url  "$MH_HOME_URL"  --home-size  "$MH_HOME_SIZE"
