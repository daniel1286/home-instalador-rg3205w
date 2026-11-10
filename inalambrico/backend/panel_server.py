#!/usr/bin/env python3
"""
Panel web para la migracion del dispositivo stock a Home.
Puerto 8080. Solo libreria estandar de Python 3.
"""

import json
import os
import re
import subprocess
import time
from http.server import BaseHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn

SERVICES = [
    ("mh-sni",      "HTTPS/SNI",      "443"),
    ("mh-captive",  "Captive Portal",  "80"),
    ("mh-ntp",      "NTP",            "123"),
    ("dnsmasq",     "DNS/DHCP",        "53"),
    ("hostapd",     "WiFi AP",        "wlan0"),
    ("mh-cwmp",     "TR-069",         "7016"),
    ("mh-cwmp-http","TR-069 HTTP",    "7006"),
    ("mh-ota",      "OTA Server",     "8000"),
]

HOSTAPD_CONF = "/etc/hostapd/hostapd.conf"
LEASES = "/var/lib/misc/dnsmasq.leases"
OTA_DIR = "/opt/mh-fake/otas"
LOG_UNITS = ["mh-sni", "mh-captive", "mh-cwmp", "mh-cwmp-http",
             "mh-ota", "mh-ntp", "dnsmasq", "hostapd"]

_dir = os.path.dirname(os.path.abspath(__file__))
_html = os.path.join(_dir, "panel.html")

_ping_cache = {}


def run(cmd, timeout=15):
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        return p.returncode, p.stdout, p.stderr
    except Exception as e:
        return 1, "", str(e)


def svc_active(name):
    _, out, _ = run(["systemctl", "is-active", name])
    return out.strip() == "active"


def get_wifi():
    ssid, pw = "Home-migracion", "homeupdate"
    try:
        with open(HOSTAPD_CONF) as f:
            for line in f:
                if line.startswith("ssid="):
                    ssid = line.split("=", 1)[1].strip()
                elif line.startswith("wpa_passphrase="):
                    pw = line.split("=", 1)[1].strip()
    except OSError:
        pass
    return {"ssid": ssid, "pass": pw}


def set_wifi(ssid, pw):
    conf = "\n".join([
        "interface=wlan0", "driver=nl80211", "ssid=%s" % ssid,
        "country_code=ES", "wmm_enabled=1", "auth_algs=1",
        "ignore_broadcast_ssid=0", "wpa=2", "wpa_passphrase=%s" % pw,
        "wpa_key_mgmt=WPA-PSK", "rsn_pairwise=CCMP",
        "hw_mode=a", "channel=36", "ieee80211d=1", "ieee80211h=1",
        "ieee80211n=1", "ieee80211ac=1",
        "ht_capab=[HT40+][SHORT-GI-20][SHORT-GI-40]",
        "vht_oper_chwidth=1", "vht_oper_centr_freq_seg0_idx=42",
    ]) + "\n"
    with open(HOSTAPD_CONF, "w") as f:
        f.write(conf)
    return run(["systemctl", "restart", "hostapd"], timeout=30)


def is_alive(ip):
    now = time.time()
    cached = _ping_cache.get(ip)
    if cached and now - cached[1] < 12:
        return cached[0]
    rc, _, _ = run(["ping", "-c", "1", "-W", "1", ip], timeout=3)
    alive = rc == 0
    _ping_cache[ip] = (alive, now)
    return alive


def get_device():
    try:
        with open(LEASES) as f:
            for line in f:
                p = line.split()
                if len(p) >= 4:
                    ip = p[2]
                    if ip.startswith("10.42.0.") and ip != "10.42.0.1":
                        ok = is_alive(ip)
                        return {"ok": ok, "ip": ip, "mac": p[1],
                                "host": p[3] if p[3] != "*" else ""}
    except OSError:
        pass
    return {"ok": False}


def get_ota():
    info = {"ready": False, "downloading": False, "phase": "waiting"}
    if not os.path.isdir(OTA_DIR):
        return info
    for f in os.listdir(OTA_DIR):
        if f.endswith(".zip"):
            path = os.path.join(OTA_DIR, f)
            sz = os.path.getsize(path)
            info["ready"] = True
            info["file"] = f
            info["size"] = sz
            info["size_human"] = "%.0f MB" % (sz / 1048576)
            break

    _, sni_out, _ = run(["journalctl", "-n", "100", "--no-pager",
                         "-u", "mh-ota", "-u", "mh-sni"], timeout=5)
    if sni_out:
        for line in sni_out.splitlines():
            lo = line.lower()
            if "pivot_ota" in lo and ("get" in lo or "serving" in lo):
                info["downloading"] = True
            if "transfercomplete" in lo:
                info["phase"] = "installing"
            if "home2" in lo and "boot" in lo:
                info["phase"] = "done"
    return info


def get_ntp_time():
    _, out, _ = run(["date", "+%s"])
    try:
        return int(out.strip())
    except (ValueError, TypeError):
        return int(time.time())


def get_log():
    _, out, _ = run(["journalctl", "-n", "40", "--no-pager",
                     "-o", "short", "--no-hostname"] +
                    [x for u in LOG_UNITS for x in ["-u", u]], timeout=5)
    lines = []
    for line in (out or "").splitlines():
        line = line.strip()
        if line:
            clean = re.sub(r"^[A-Z][a-z]{2} \d+ ", "", line)
            lines.append(clean)
    return lines


def load_html():
    try:
        with open(_html, "rb") as f:
            return f.read()
    except OSError:
        return b"<h1>panel.html no encontrado</h1>"


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        pass

    def _json(self, data, code=200):
        body = json.dumps(data).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", len(body))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(body)

    def _read_body(self):
        length = int(self.headers.get("Content-Length", 0))
        if length:
            return json.loads(self.rfile.read(length))
        return {}

    def do_GET(self):
        path = self.path.split("?")[0]
        if path == "/" or path == "/panel":
            page = load_html()
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", len(page))
            self.end_headers()
            self.wfile.write(page)
            return

        if path == "/api/status":
            svcs = []
            for name, label, port in SERVICES:
                svcs.append({"id": name, "name": label, "port": port,
                             "active": svc_active(name)})
            self._json({
                "services": svcs,
                "device": get_device(),
                "wifi": get_wifi(),
                "ota": get_ota(),
                "ntp_time": get_ntp_time(),
            })
            return

        if path == "/api/log":
            self._json({"lines": get_log()})
            return

        self.send_error(404)

    def do_POST(self):
        path = self.path.split("?")[0]
        if path == "/api/wifi":
            body = self._read_body()
            ssid = body.get("ssid", "").strip()
            pw = body.get("pass", "").strip()
            if not ssid or len(pw) < 8:
                self._json({"ok": False, "error": "SSID vacio o clave < 8 chars"}, 400)
                return
            rc, _, err = set_wifi(ssid, pw)
            self._json({"ok": rc == 0, "error": err if rc else ""})
            return

        if path == "/api/action":
            body = self._read_body()
            act = body.get("action", "")
            if act == "restart_wifi":
                rc, _, _ = run(["systemctl", "restart", "hostapd"], timeout=30)
                self._json({"ok": rc == 0})
            elif act == "restart_all":
                ok = True
                for name, _, _ in SERVICES:
                    rc, _, _ = run(["systemctl", "restart", name], timeout=15)
                    if rc != 0:
                        ok = False
                self._json({"ok": ok})
            else:
                self._json({"ok": False, "error": "accion desconocida"}, 400)
            return

        self.send_error(404)

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET,POST,OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()


class ThreadedServer(ThreadingMixIn, HTTPServer):
    daemon_threads = True


if __name__ == "__main__":
    port = int(os.environ.get("PORT", 8080))
    srv = ThreadedServer(("0.0.0.0", port), Handler)
    print("Panel en http://0.0.0.0:%d" % port)
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass
