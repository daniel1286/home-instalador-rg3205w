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
PIVOT_OTA="pivot_ota_rg3205w_home.zip"
HOME_OTA="Home-ota-rg3205w.zip"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log()  { echo -e "${GREEN}[+]${NC} $1"; }
warn() { echo -e "${YELLOW}[!]${NC} $1"; }
err()  { echo -e "${RED}[x]${NC} $1"; exit 1; }

[ "$(id -u)" -ne 0 ] && err "Ejecutar como root: sudo bash instalar.sh"
[ "$(uname)" != "Linux" ] && err "Solo compatible con Linux"
[ ! -d "$BACKEND_DIR" ] && err "No se encuentra $BACKEND_DIR"

echo ""
echo "================================================="
echo "  Backend inalambrico — Askey RG3205W a Home"
echo "  LineageOS 22.x / Android 15"
echo "================================================="
echo ""

# 1. Paquetes
log "Instalando paquetes..."
apt-get update -qq
apt-get install -y -qq \
    hostapd dnsmasq \
    python3 python3-cryptography \
    iptables wget

# 2. Parar servicios en conflicto
log "Preparando servicios..."
systemctl stop systemd-timesyncd 2>/dev/null || true
systemctl disable systemd-timesyncd 2>/dev/null || true
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

# 5. Configurar hostapd (WiFi AP 5 GHz)
log "Configurando punto de acceso WiFi (5 GHz, canal 36)..."
cp "$BACKEND_DIR/config/hostapd.conf" /etc/hostapd/hostapd.conf
sed -i 's|^#\?DAEMON_CONF=.*|DAEMON_CONF="/etc/hostapd/hostapd.conf"|' /etc/default/hostapd 2>/dev/null || true

# 6. Configurar dnsmasq (DNS + DHCP)
log "Configurando DNS y DHCP..."
[ -f /etc/dnsmasq.conf ] && cp /etc/dnsmasq.conf /etc/dnsmasq.conf.bak
cp "$BACKEND_DIR/config/dnsmasq.conf" /etc/dnsmasq.conf
echo "dhcp-script=$INSTALL_DIR/dhcp-tls-gate.sh" >> /etc/dnsmasq.conf

# 7. Configuracion del backend
log "Instalando configuracion..."
cp "$BACKEND_DIR/config/backend.default" /etc/default/mh-fake

# 8. IP estatica en wlan0
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

# 9. Servicios systemd
log "Instalando servicios systemd..."
for f in "$BACKEND_DIR/systemd/"mh-*.service; do
    cp "$f" /etc/systemd/system/
done
systemctl daemon-reload

# 10. Generar certificados TLS
log "Generando certificados TLS..."
cd "$INSTALL_DIR"
python3 generate_certs.py

# 11. Habilitar servicios
log "Habilitando servicios..."
for svc in mh-wlan-ip mh-sni mh-captive mh-ntp mh-cwmp mh-cwmp-http mh-ota mh-panel; do
    systemctl enable "$svc" 2>/dev/null
done
systemctl enable hostapd dnsmasq

# 12. Descargar OTAs si no existen
if [ ! -f "$INSTALL_DIR/otas/$PIVOT_OTA" ]; then
    log "Descargando OTA pivot desde GitHub Releases..."
    wget -q --show-progress -O "$INSTALL_DIR/otas/$PIVOT_OTA" \
        "$REPO_URL/releases/latest/download/$PIVOT_OTA" 2>/dev/null && {
        log "OTA pivot descargado correctamente."
    } || {
        warn "No se pudo descargar el pivot."
        warn "Descargalo de: $REPO_URL/releases"
        warn "Y copialo a: $INSTALL_DIR/otas/$PIVOT_OTA"
    }
fi

if [ ! -f "$INSTALL_DIR/otas/$HOME_OTA" ]; then
    log "Descargando Home ROM desde GitHub Releases (~1 GB, puede tardar)..."
    wget -q --show-progress -O "$INSTALL_DIR/otas/$HOME_OTA" \
        "$REPO_URL/releases/latest/download/$HOME_OTA" 2>/dev/null && {
        log "Home ROM descargado correctamente."
    } || {
        warn "No se pudo descargar el Home ROM."
        warn "Descargalo de: $REPO_URL/releases"
        warn "Y copialo a: $INSTALL_DIR/otas/$HOME_OTA"
    }
fi

echo ""
echo "================================================="
echo "  Instalacion completada"
echo ""
echo "  WiFi:    Home-migracion / homeupdate"
echo "  Canal:   36 (5 GHz)"
echo "  Panel:   http://10.42.0.1:8080"
echo ""
if [ -f "$INSTALL_DIR/otas/$PIVOT_OTA" ] && [ -f "$INSTALL_DIR/otas/$HOME_OTA" ]; then
    echo "  OTAs:    Pivot + Home ROM (listos)"
elif [ -f "$INSTALL_DIR/otas/$PIVOT_OTA" ]; then
    echo "  OTAs:    Pivot (listo) / Home ROM (PENDIENTE)"
    echo "           Descargalo de: $REPO_URL/releases"
else
    echo "  OTAs:    Pendientes de descargar"
    echo "           $REPO_URL/releases"
fi
echo ""
echo "  Reiniciar para activar:"
echo "    sudo reboot"
echo ""
echo "  Tras el reinicio, el backend arranca solo"
echo "  y queda esperando conexiones del dispositivo."
echo "================================================="
