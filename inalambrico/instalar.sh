#!/bin/bash
# instalar.sh — Configura una Raspberry Pi como backend inalambrico
# para migrar el Askey RG3205W a Home (LineageOS 22.x / Android 15).
#
# Uso:
#   sudo bash instalar.sh
#
# Requisitos:
#   - Raspberry Pi 3B+ o superior (WiFi 5 GHz)
#   - Raspberry Pi OS (Lite recomendado)
#   - Conexion a internet por ethernet (para instalar paquetes)

set -euo pipefail

INSTALL_DIR="/opt/mh-fake"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKEND_DIR="$SCRIPT_DIR/backend"
REPO_URL="https://github.com/daniel1286/home-instalador-rg3205w"
HOME_OTA="Home-ota-rg3205w.zip"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log()  { echo -e "${GREEN}[+]${NC} $1"; }
warn() { echo -e "${YELLOW}[!]${NC} $1"; }
err()  { echo -e "${RED}[x]${NC} $1"; exit 1; }

# Reintenta una orden hasta tres veces: la red de una Pi recien instalada falla a veces
reintentar() {
    local i
    for i in 1 2 3; do
        "$@" && return 0
        warn "Fallo (intento $i de 3). Reintento en 5 s..."
        sleep 5
    done
    return 1
}

[ "$(id -u)" -ne 0 ] && err "Ejecutar como root: sudo bash instalar.sh"
[ "$(uname)" != "Linux" ] && err "Solo compatible con Linux"
[ ! -d "$BACKEND_DIR" ] && err "No se encuentra $BACKEND_DIR"

echo ""
echo "================================================="
echo "  Backend inalambrico — Askey RG3205W a Home"
echo "  LineageOS 22.x / Android 15"
echo "================================================="
echo ""

# 1. Paquetes (git y las herramientas de WiFi entre ellos, para que valga en una Pi limpia)
log "Instalando paquetes..."
reintentar apt-get update ||
    err "No se pudo actualizar la lista de paquetes. Conecta la Pi a internet por ethernet y repite."
reintentar apt-get install -y \
    hostapd dnsmasq \
    python3 python3-cryptography \
    iptables wget ca-certificates \
    git rfkill iw ||
    err "No se pudieron instalar los paquetes. Mira el error de arriba (suele ser la red)."

# 2. Parar servicios en conflicto
log "Preparando servicios..."
# La Pi mantiene su propia hora en punto (la del Askey la da ntp_server por su cuenta, en wlan0)
systemctl enable --now systemd-timesyncd 2>/dev/null || true
systemctl unmask hostapd 2>/dev/null || true
systemctl stop hostapd dnsmasq 2>/dev/null || true

# 3. Crear estructura
log "Creando directorios en $INSTALL_DIR..."
mkdir -p "$INSTALL_DIR"/{certs,otas,video,config}

# 4. Copiar archivos del backend
log "Copiando archivos..."
for f in sni_server.py cwmp_server.py captive_server.py ota_server.py \
         ntp_server.py generate_certs.py panel.html panel_server.py; do
    cp "$BACKEND_DIR/$f" "$INSTALL_DIR/"
done
for f in run-sni.sh run-cwmp.sh run-cwmp-http.sh dhcp-tls-gate.sh; do
    cp "$BACKEND_DIR/$f" "$INSTALL_DIR/"
    chmod +x "$INSTALL_DIR/$f"
done

# 5. Radio WiFi: desbloquearla, fijar el pais y comprobar que esta Pi hace AP en 5 GHz
log "Preparando la radio WiFi (5 GHz)..."
rfkill unblock wifi 2>/dev/null || true
iw reg set ES 2>/dev/null || true
# Que el pais quede fijado tambien tras reiniciar
command -v raspi-config >/dev/null 2>&1 && raspi-config nonint do_wifi_country ES 2>/dev/null || true

PHY=$(iw dev wlan0 info 2>/dev/null | awk '/wiphy/ {print "phy"$2}')
CAPS=$([ -n "$PHY" ] && iw phy "$PHY" info 2>/dev/null || true)
if [ -n "$CAPS" ]; then
    echo "$CAPS" | grep -qE '\* AP' || warn "Este WiFi no anuncia modo punto de acceso; hostapd podria no arrancar."
    # Solo se da por seguro que no hay 5 GHz si se leyo bien la banda de 2,4 pero no la de 5
    if echo "$CAPS" | grep -qE '24[0-9]{2} MHz' && ! echo "$CAPS" | grep -qE '5[0-9]{3} MHz'; then
        err "El WiFi de esta Pi no tiene 5 GHz. El RG3205W solo conecta en 5 GHz: usa una Pi 3B+, 4, 5 o Zero 2 W."
    fi
else
    warn "No se pudo leer el WiFi (wlan0). Comprueba que la Pi tiene WiFi y que no esta bloqueado."
fi

# 6. Configurar hostapd (WiFi AP 5 GHz)
log "Configurando punto de acceso WiFi (5 GHz, canal 36)..."
cp "$BACKEND_DIR/config/hostapd.conf" /etc/hostapd/hostapd.conf
sed -i 's|^#\?DAEMON_CONF=.*|DAEMON_CONF="/etc/hostapd/hostapd.conf"|' /etc/default/hostapd 2>/dev/null || true

# 7. Configurar dnsmasq (DNS + DHCP)
log "Configurando DNS y DHCP..."
[ -f /etc/dnsmasq.conf ] && cp /etc/dnsmasq.conf /etc/dnsmasq.conf.bak
cp "$BACKEND_DIR/config/dnsmasq.conf" /etc/dnsmasq.conf
echo "dhcp-script=$INSTALL_DIR/dhcp-tls-gate.sh" >> /etc/dnsmasq.conf

# 8. Configuracion del backend
log "Instalando configuracion..."
cp "$BACKEND_DIR/config/backend.default" /etc/default/mh-fake

# 9. IP estatica en wlan0
log "Configurando red (10.42.0.1/24 en wlan0)..."
if [ -f /etc/dhcpcd.conf ]; then
    if ! grep -q "interface wlan0" /etc/dhcpcd.conf; then
        cat >> /etc/dhcpcd.conf <<'DHCP'

# Backend inalambrico - IP estatica
interface wlan0
    static ip_address=10.42.0.1/24
    nohook wpa_supplicant
DHCP
    fi
elif command -v nmcli &>/dev/null; then
    nmcli con delete "home-backend" 2>/dev/null || true
    nmcli device set wlan0 managed no 2>/dev/null || true
    mkdir -p /etc/NetworkManager/conf.d
    cat > /etc/NetworkManager/conf.d/99-home-wlan0.conf <<'NM'
[keyfile]
unmanaged-devices=interface-name:wlan0
NM
    nmcli general reload 2>/dev/null || true
    ip addr flush dev wlan0 2>/dev/null || true
    ip addr add 10.42.0.1/24 dev wlan0 2>/dev/null || true
else
    warn "No se encontro dhcpcd ni NetworkManager."
    warn "Configura IP 10.42.0.1/24 en wlan0 manualmente."
fi

sysctl -w net.ipv4.ip_forward=1 >/dev/null
echo "net.ipv4.ip_forward=1" > /etc/sysctl.d/99-home-backend.conf

# 10. Servicios systemd
log "Instalando servicios systemd..."
for f in "$BACKEND_DIR/systemd/"mh-*.service; do
    cp "$f" /etc/systemd/system/
done
systemctl daemon-reload

# 11. Generar certificados TLS
log "Generando certificados TLS..."
cd "$INSTALL_DIR"
python3 generate_certs.py

# 12. Habilitar servicios
log "Habilitando servicios..."
for svc in mh-wlan-ip mh-sni mh-captive mh-ntp mh-cwmp mh-cwmp-http mh-ota mh-panel; do
    systemctl enable "$svc" 2>/dev/null
done
systemctl enable hostapd dnsmasq

# 13. Descargar la OTA de la version publicada (la que diga el Worker) y comprobar su huella
WORKER_URL="${MH_WORKER_URL:-https://home-rom.home1286.workers.dev/v1/ota/rg3205w.json}"
OTA_DEST="$INSTALL_DIR/otas/$HOME_OTA"

descargar_ota() {
    log "Preguntando al Worker por la version publicada..."
    local meta url="" sha="" version=""
    meta=$(wget -q -O - "$WORKER_URL" 2>/dev/null) || meta=""
    if [ -n "$meta" ]; then
        # La completa es el paquete sin "desde"; de ahi salen su url y su sha256
        read -r version url sha < <(printf '%s' "$meta" | python3 -c '
import json, sys
try:
    d = json.load(sys.stdin)
    c = next((p for p in d.get("paquetes", []) if "desde" not in p), d)
    print(d.get("version",""), c.get("url",""), c.get("sha256",""))
except Exception:
    pass
') || true
    fi
    if [ -z "$url" ]; then
        warn "No se pudo leer el Worker. Uso la ultima Release de GitHub (sin huella que comprobar)."
        url="$REPO_URL/releases/latest/download/$HOME_OTA"
    else
        log "Version publicada: Home $version."
        # La descarga es de la Pi, no de la app Actualizaciones: que el Worker la cuente como tal
        url="${url/origen=app/origen=pi}"
    fi
    # Para el contador de ota_server: la version que se sirve (si no se sabe, no se cuenta)
    [ -n "$version" ] && printf '%s' "$version" > "$INSTALL_DIR/otas/version.txt"
    # Si ya esta la correcta (su huella cuadra), no se baja otra vez
    if [ -n "$sha" ] && [ -f "$OTA_DEST" ] &&
       [ "$(sha256sum "$OTA_DEST" | cut -d' ' -f1)" = "$sha" ]; then
        log "La OTA ya esta y su huella cuadra. Saltando."
        return 0
    fi
    rm -f "$OTA_DEST"
    log "Descargando la OTA..."
    reintentar wget --no-verbose -O "$OTA_DEST" "$url" ||
        { rm -f "$OTA_DEST"; warn "No se pudo descargar la OTA."; return 1; }
    local sz; sz=$(stat -c%s "$OTA_DEST" 2>/dev/null || echo 0)
    [ "$sz" -lt 1000 ] && { rm -f "$OTA_DEST"; warn "OTA corrupta ($sz bytes)."; return 1; }
    if [ -n "$sha" ]; then
        if [ "$(sha256sum "$OTA_DEST" | cut -d' ' -f1)" != "$sha" ]; then
            rm -f "$OTA_DEST"
            err "La huella de la OTA no cuadra con la del Worker: no se sirve una imagen sin verificar."
        fi
        log "Huella verificada."
    fi
    log "OTA lista ($(du -h "$OTA_DEST" | cut -f1))."
}

descargar_ota || {
    warn "Descargala de: $REPO_URL/releases"
    warn "Y copiala a: $OTA_DEST"
}

echo ""
echo "================================================="
echo "  Instalacion completada"
echo ""
echo "  WiFi:    Home-migracion / homeupdate"
echo "  Canal:   36 (5 GHz)"
echo "  Panel:   http://10.42.0.1:8080"
echo ""
if [ -f "$INSTALL_DIR/otas/$HOME_OTA" ]; then
    echo "  OTAs:    Home ROM (listo)"
else
    echo "  OTAs:    Pendiente de descargar"
    echo "           $REPO_URL/releases"
fi
echo ""
echo "  Reiniciar para activar:"
echo "    sudo reboot"
echo ""
echo "  Tras el reinicio, el backend arranca solo"
echo "  y queda esperando conexiones del dispositivo."
echo "================================================="
