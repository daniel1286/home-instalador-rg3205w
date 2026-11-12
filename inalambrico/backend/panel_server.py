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
MEDIA_DIR = "/opt/mh-fake/media"
MH_DEFAULT = "/etc/default/mh-fake"
MODE_UNITS = ["mh-sni", "mh-cwmp", "mh-cwmp-http"]


def get_mode():
    try:
        with open(MH_DEFAULT) as f:
            for line in f:
                line = line.strip()
                if line.startswith("MH_MODE="):
                    v = line.split("=", 1)[1].strip().strip('"').strip("'")
                    return v or "ota"
    except OSError:
        pass
    return "ota"


def set_mode(mode):
    if mode not in ("ota", "onboarding"):
        return 1, "modo invalido"
    try:
        lines, found = [], False
        with open(MH_DEFAULT) as f:
            for line in f:
                if line.strip().startswith("MH_MODE="):
                    lines.append("MH_MODE=%s\n" % mode)
                    found = True
                else:
                    lines.append(line)
        if not found:
            lines.append("MH_MODE=%s\n" % mode)
        with open(MH_DEFAULT, "w") as f:
            f.writelines(lines)
    except OSError as e:
        return 1, str(e)
    ok = True
    for svc in MODE_UNITS:
        rc, _, _ = run(["systemctl", "restart", svc], timeout=20)
        if rc != 0:
            ok = False
    return (0 if ok else 1), ""
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


DL_PROGRESS = os.path.join(OTA_DIR, ".dl_progress.json")


def get_dl_progress():
    try:
        with open(DL_PROGRESS) as f:
            p = json.load(f)
    except Exception:
        return None
    try:
        sent = int(p.get("sent", 0))
        total = int(p.get("total", 0) or 0)
        started = float(p.get("started", 0))
        updated = float(p.get("updated", 0))
    except (ValueError, TypeError):
        return None
    age = time.time() - updated
    if not (p.get("active") and age < 8 and total > 0 and 0 < sent < total):
        return None
    dur = max(0.001, updated - started)
    bps = sent / dur
    return {"pct": int(sent * 100 / total), "sent": sent, "total": total,
            "bps": bps, "eta": int((total - sent) / bps) if bps > 0 else 0,
            "file": p.get("file")}


def configured_ota():
    """Nombre del fichero OTA que sirve el backend (basename de MH_PIVOT_URL)."""
    try:
        with open(MH_DEFAULT) as f:
            for line in f:
                line = line.strip()
                if line.startswith("MH_PIVOT_URL="):
                    url = line.split("=", 1)[1].strip().strip('"').strip("'")
                    return url.rsplit("/", 1)[-1]
    except OSError:
        pass
    return None


def human_size(n):
    if n >= 1073741824:
        return "%.2f GB" % (n / 1073741824)
    if n >= 1048576:
        return "%.0f MB" % (n / 1048576)
    return "%.0f KB" % (n / 1024)


def get_ota():
    info = {"ready": False, "downloading": False, "phase": "waiting"}
    if not os.path.isdir(OTA_DIR):
        return info
    # Mostrar la OTA real que sirve el backend (MH_PIVOT_URL); si no, la mayor.
    want = configured_ota()
    best = None
    for f in os.listdir(OTA_DIR):
        if not f.endswith(".zip"):
            continue
        try:
            sz = os.path.getsize(os.path.join(OTA_DIR, f))
        except OSError:
            continue
        if sz < 1000:
            continue
        if want and f == want:
            best = (f, sz)
            break
        if best is None or sz > best[1]:
            best = (f, sz)
    if best:
        info["ready"] = True
        info["file"] = best[0]
        info["size"] = best[1]
        info["size_human"] = human_size(best[1])

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

    prog = get_dl_progress()
    if prog:
        info["downloading"] = True
        info["pct"] = prog["pct"]
        info["dl_sent"] = prog["sent"]
        info["dl_total"] = prog["total"]
        info["dl_bps"] = prog["bps"]
        info["dl_eta"] = prog["eta"]
    return info


def get_ntp_time():
    _, out, _ = run(["date", "+%s"])
    try:
        return int(out.strip())
    except (ValueError, TypeError):
        return int(time.time())


NOISE_PATTERNS = re.compile(r"aura|nuance|directline|msasr", re.IGNORECASE)
# ISO: "2026-11-11T02:26:38+0100 unidad[pid]: mensaje"
_ISO_RE = re.compile(r"^\d{4}-\d{2}-\d{2}T(\d{2}:\d{2}:\d{2})\S*\s+(.*)$")
_UNIT_RE = re.compile(r"^[\w.\-]+\[\d+\]:\s*")          # "run-sni.sh[885]: "
_APPDATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}[,.]\d+\s*")  # fecha duplicada de la app

def get_log():
    # Registros reales de los servicios, limpiados para que sean legibles
    # (hora + mensaje, sin el prefijo de systemd ni la fecha duplicada).
    _, out, _ = run(["journalctl", "-n", "90", "--no-pager",
                     "-o", "short-iso", "--no-hostname"] +
                    [x for u in LOG_UNITS for x in ["-u", u]], timeout=5)
    lines = []
    for raw in (out or "").splitlines():
        raw = raw.rstrip()
        if not raw or NOISE_PATTERNS.search(raw):
            continue
        m = _ISO_RE.match(raw)
        if not m:
            continue
        hhmmss, rest = m.group(1), m.group(2)
        rest = _UNIT_RE.sub("", rest)        # quita "unidad[pid]: "
        rest = _APPDATE_RE.sub("", rest)     # quita la fecha ISO que repite la app
        rest = rest.strip()
        if not rest:
            continue
        lines.append("%s  %s" % (hhmmss, rest))
    return lines[-50:]


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
                "mode": get_mode(),
                "ntp_time": get_ntp_time(),
            })
            return

        if path == "/api/log":
            self._json({"lines": get_log()})
            return

        if path.startswith("/media/"):
            fname = os.path.basename(path)  # evita path traversal
            fpath = os.path.join(MEDIA_DIR, fname)
            if fname and os.path.isfile(fpath):
                ext = fname.rsplit(".", 1)[-1].lower()
                ctype = {"png": "image/png", "jpg": "image/jpeg",
                         "jpeg": "image/jpeg", "svg": "image/svg+xml",
                         "webp": "image/webp", "gif": "image/gif",
                         "mp4": "video/mp4", "webm": "video/webm"}.get(ext, "application/octet-stream")
                with open(fpath, "rb") as f:
                    data = f.read()
                self.send_response(200)
                self.send_header("Content-Type", ctype)
                self.send_header("Content-Length", len(data))
                self.send_header("Cache-Control", "public, max-age=86400")
                self.end_headers()
                self.wfile.write(data)
                return
            self.send_error(404)
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
            elif act == "service":
                name = body.get("name", "")
                op = body.get("op", "toggle")
                valid = {s[0] for s in SERVICES}
                if name not in valid:
                    self._json({"ok": False, "error": "servicio desconocido"}, 400)
                    return
                if op == "toggle":
                    op = "stop" if svc_active(name) else "start"
                if op not in ("start", "stop", "restart"):
                    self._json({"ok": False, "error": "operacion invalida"}, 400)
                    return
                rc, _, err = run(["systemctl", op, name], timeout=25)
                self._json({"ok": rc == 0, "op": op, "active": svc_active(name),
                            "error": err.strip() if rc else ""})
            elif act == "set_mode":
                rc, err = set_mode(body.get("mode", ""))
                self._json({"ok": rc == 0, "mode": get_mode(), "error": err})
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
