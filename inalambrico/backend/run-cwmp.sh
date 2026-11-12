#!/usr/bin/env bash
# Lanzador del ACS CWMP/TR-069 (:7016 TLS). Lee /etc/default/mh-fake.
# El ACS siempre responde Inform; en modo ota ademas encola el Download.
set -eu
[ -f /etc/default/mh-fake ] && . /etc/default/mh-fake
MH_PIVOT_URL=${MH_PIVOT_URL:-https://main.acs.telefonica.net:7016/ota/pivot_ota_rg3205w_home.zip}
MH_PIVOT_SIZE=${MH_PIVOT_SIZE:-33845013}
MH_HOME_URL=${MH_HOME_URL:-http://10.42.0.1:8000/Home-ota-rg3205w.zip}
MH_HOME_SIZE=${MH_HOME_SIZE:-1108418366}
MH_ACS_AUTH=${MH_ACS_AUTH:-}

OTA_DIR=/opt/mh-fake/otas
for ota_file in pivot_ota_rg3205w_home.zip Home-ota-rg3205w.zip; do
    fpath="$OTA_DIR/$ota_file"
    if [ ! -f "$fpath" ] || [ "$(stat -c%s "$fpath" 2>/dev/null || echo 0)" -lt 1000 ]; then
        echo "[WARN] $ota_file no existe o esta vacio en $OTA_DIR" >&2
    fi
done

PIVOT_PATH="$OTA_DIR/$(basename "$MH_PIVOT_URL")"
[ -f "$PIVOT_PATH" ] && [ "$(stat -c%s "$PIVOT_PATH")" -gt 1000 ] && MH_PIVOT_SIZE=$(stat -c%s "$PIVOT_PATH")
HOME_PATH="$OTA_DIR/$(basename "$MH_HOME_URL")"
[ -f "$HOME_PATH" ] && [ "$(stat -c%s "$HOME_PATH")" -gt 1000 ] && MH_HOME_SIZE=$(stat -c%s "$HOME_PATH")

exec /usr/bin/python3 /opt/mh-fake/cwmp_server.py \
    --bind 0.0.0.0:7016 \
    --cert /opt/mh-fake/certs/main.acs.telefonica.net.crt \
    --key  /opt/mh-fake/certs/main.acs.telefonica.net.key \
    --auth "$MH_ACS_AUTH" \
    --pivot-url "$MH_PIVOT_URL" --pivot-size "$MH_PIVOT_SIZE" \
    --home-url  "$MH_HOME_URL"  --home-size  "$MH_HOME_SIZE"
