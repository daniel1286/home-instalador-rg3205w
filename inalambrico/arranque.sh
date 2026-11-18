#!/bin/bash
# arranque.sh — Pone en marcha el backend inalambrico en una Raspberry Pi
# recien instalada, aunque no tenga git. Instala git, descarga el proyecto y
# ejecuta instalar.sh. Pensado para una sola linea.
#
# Uso (como root):
#   curl -fsSL https://raw.githubusercontent.com/daniel1286/home-instalador-rg3205w/main/inalambrico/arranque.sh | sudo bash
set -euo pipefail

REPO_URL="https://github.com/daniel1286/home-instalador-rg3205w"
DEST="/opt/home-instalador"

[ "$(id -u)" -ne 0 ] && { echo "Ejecuta como root (anade sudo delante)."; exit 1; }
[ "$(uname)" != "Linux" ] && { echo "Solo para Raspberry Pi OS (Linux)."; exit 1; }

echo "[+] Instalando git..."
apt-get update
apt-get install -y git

if [ -d "$DEST/.git" ]; then
    echo "[+] Actualizando el proyecto..."
    git -C "$DEST" pull --ff-only || true
else
    echo "[+] Descargando el proyecto..."
    rm -rf "$DEST"
    git clone --depth 1 "$REPO_URL" "$DEST"
fi

echo "[+] Arrancando el instalador..."
cd "$DEST/inalambrico"
exec bash instalar.sh
