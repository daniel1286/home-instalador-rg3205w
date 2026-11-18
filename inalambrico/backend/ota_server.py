#!/usr/bin/env python3
"""
ota_server.py — Plain HTTP file server for OTA zip downloads.

The Askey updater GETs the OTA with @Streaming over Authorization: Bearer <jwt>.
We don't verify the token (the device uses TLS trust-all anyway).

When it finishes serving the whole OTA to a device, it reports one delivered
OTA to the Home statistics (same Worker as the Windows installer): the Pi cannot
know whether the install then finished, so the result is "entregada", sent only
if the Worker lists it in "acepta". It sends no identifier, only:
path=inalambrico, installer and Home versions. Each device is reported once,
also across restarts: a hash of its MAC is kept on the Pi (never sent).
Opt-out with MH_CONTAR=0.

Usage:
  python3 ota_server.py --bind 0.0.0.0:8000 --root /srv/ota
"""
import argparse
import hashlib
import json
import logging
import os
import sys
import threading
import urllib.request
from http.server import SimpleHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn

logging.basicConfig(level=logging.INFO,
                    format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("ota")

OTA_NOMBRE = "Home-ota-rg3205w.zip"
CONTADOS = "/var/lib/mh-fake/contados.json"
_lock = threading.Lock()


def _aparato(ip):
    """Huella del aparato: su MAC (la IP cambia con cada concesion de DHCP). Si no esta en la tabla ARP, la IP."""
    try:
        with open("/proc/net/arp", encoding="ascii") as f:
            for linea in f.read().splitlines()[1:]:
                c = linea.split()
                if len(c) >= 4 and c[0] == ip and c[3] != "00:00:00:00:00:00":
                    ip = c[3].lower()
                    break
    except OSError:
        pass
    return hashlib.sha256(ip.encode()).hexdigest()


def _leer_contados():
    try:
        with open(CONTADOS, encoding="utf-8") as f:
            return set(json.load(f))
    except (OSError, ValueError):
        return set()


def _guardar_contados(contados):
    os.makedirs(os.path.dirname(CONTADOS), exist_ok=True)
    tmp = CONTADOS + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(sorted(contados), f)
    os.replace(tmp, CONTADOS)


def _acepta_entregada(cfg):
    """El Worker anuncia en su lista de versiones lo que admite /v1/instalacion."""
    url = cfg["worker"].rstrip("/") + "/v1/instalar/rg3205w.json"
    with urllib.request.urlopen(urllib.request.Request(url, headers={"user-agent": "home-backend"}), timeout=10) as r:
        return "entregada" in json.load(r).get("acepta", [])


def contar(cliente, cfg):
    """Avisa al Worker de una OTA entregada por la Pi, una vez por aparato. Nunca rompe el servicio."""
    try:
        if not cfg["contar"] or not cfg["worker"] or not cfg["version"]:
            return
        aparato = _aparato(cliente)
        with _lock:
            contados = _leer_contados()
            if aparato in contados:
                return
            if not _acepta_entregada(cfg):
                log.info("el Worker aun no admite 'entregada': no se cuenta")
                return
            datos = json.dumps({
                "v": 1, "camino": "inalambrico", "instalador": cfg["instalador"],
                "home": cfg["version"], "resultado": "entregada",
            }).encode()
            req = urllib.request.Request(
                cfg["worker"].rstrip("/") + "/v1/instalacion", data=datos,
                headers={"content-type": "application/json", "user-agent": "home-backend"})
            urllib.request.urlopen(req, timeout=10).read()
            contados.add(aparato)
            _guardar_contados(contados)
        log.info("OTA entregada por Pi contada (Home %s)", cfg["version"])
    except Exception as e:
        log.info("no se pudo contar la OTA entregada (no afecta a nada): %s", e)


class ThreadingHTTPServer(ThreadingMixIn, HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


class Handler(SimpleHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    cfg = {"contar": False, "worker": "", "version": "", "instalador": "1.0"}

    def log_message(self, fmt, *args):
        log.info("%s - %s", self.client_address[0], fmt % args)

    def do_GET(self):
        ruta = self.path.split("?", 1)[0]
        fpath = os.path.join(os.getcwd(), ruta.lstrip("/"))
        entero = os.path.isfile(fpath) and self.headers.get("Range") is None
        if os.path.isfile(fpath):
            log.info(">>> HTTP DOWNLOAD START: %s from %s (%d bytes)",
                     self.path, self.client_address[0], os.path.getsize(fpath))
        else:
            log.info(">>> HTTP REQUEST (not found): %s from %s", self.path, self.client_address[0])
        super().do_GET()
        # Entregada entera la OTA (una sola descarga, sin Range) = una instalacion por Pi
        if entero and os.path.basename(ruta) == OTA_NOMBRE:
            threading.Thread(target=contar, args=(self.client_address[0], self.cfg), daemon=True).start()


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--bind", default="0.0.0.0:8000")
    p.add_argument("--root", default="./otas", help="dir with OTA zips")
    args = p.parse_args()

    os.chdir(args.root)
    host, port = args.bind.rsplit(":", 1)
    port = int(port)

    # La version de Home que se sirve la deja instalar.sh en version.txt, junto a la OTA
    version = ""
    if os.path.isfile("version.txt"):
        with open("version.txt", encoding="utf-8") as f:
            version = f.read().strip()
    Handler.cfg = {
        "contar": os.environ.get("MH_CONTAR", "1") != "0",
        "worker": os.environ.get("MH_WORKER", "https://home-rom.home1286.workers.dev"),
        "version": version,
        "instalador": os.environ.get("MH_INSTALADOR", "1.0"),
    }

    httpd = ThreadingHTTPServer((host, port), Handler)
    log.info("ota_server listening on http://%s:%d root=%s", host, port, os.path.abspath(args.root))
    if not Handler.cfg["version"]:
        log.info("sin version.txt: las instalaciones por Pi no se contaran hasta la proxima pasada de instalar.sh")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        log.info("bye")


if __name__ == "__main__":
    sys.exit(main())
