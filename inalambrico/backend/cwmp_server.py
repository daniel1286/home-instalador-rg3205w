#!/usr/bin/env python3
"""
cwmp_server.py — Servidor ACS SOAP local para TR-069 (Askey RG3205W).

Listens on TLS port 7016 (or HTTP 7006 with --no-tls), path /cwmpWeb/WGCPEMgt.
Accepts Basic Auth (default ACS1234:ACS1234, or ANY if --auth is empty).
Accepts Inform SOAP envelopes.

Session logic (two-cycle OTA):

  Cycle 1 (serve pivot):
    CPE -----POST Inform BOOTSTRAP/BOOT----> ACS   (stage AWAITING_INFORM)
        <----InformResponse-----             (stage INFORM_SEEN)
    CPE -----POST empty------>               (stage INFORM_SEEN)
        <----Download RPC (pivot)-----       (stage DOWNLOAD_SENT)
    CPE  (downloads pivot, verifies, applies, reboot to recovery, reboot normal)
    CPE -----POST TransferComplete---->      (stage TRANSFER_COMPLETE)
        <----TransferCompleteResponse------
    CPE -----POST empty------>
        <----(end session, pivot_count++)

  Cycle 2 (serve home-OTA):
    CPE -----POST Inform 1 BOOT after pivot---->
        <----InformResponse-----
    CPE -----POST empty------>
        <----Download RPC (home-OTA)-----     (because pivot_count > 0)
    CPE  (downloads home-OTA, verifies, applies, Home 2 instalado)

Session state is global, not per-IP, because the CPE's IP may change after
reboot (DHCP renew). We count TransferComplete events: 0 => serve pivot,
>=1 => serve home-OTA (if provided).

Usage:
  python3 cwmp_server.py --bind 0.0.0.0:7016 \
                         --cert certs/main.acs.telefonica.net.crt \
                         --key  certs/main.acs.telefonica.net.key \
                         --pivot-url http://10.42.0.1:8000/pivot_ota_rg3205w_home.zip \
                         --pivot-size 1128435 \
                         --home-url http://10.42.0.1:8000/Home-ota-rg3205w.zip \
                         --home-size 1150000000 \
                         --auth ""   # empty = accept any credentials
"""

import argparse
import base64
import logging
import re
import ssl
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn

logging.basicConfig(level=logging.INFO,
                    format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("cwmp")


# Global state (safe for a single-tenant Askey install)
STATE = {
    "transfer_complete_count": 0,
    "cycle": 1,
    "last_cpe_ip": None,
    "last_cpe_serial": None,
    "sessions": {},    # ip -> dict(stage, cwmp_id)
    "pivot_served_at": None,
    "home_served_at": None,
}
STATE_LOCK = threading.Lock()


def inform_response(cwmp_id):
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"
                  xmlns:cwmp="urn:dslforum-org:cwmp-1-0">
 <soapenv:Header>
  <cwmp:ID soapenv:mustUnderstand="1">{cwmp_id}</cwmp:ID>
 </soapenv:Header>
 <soapenv:Body>
  <cwmp:InformResponse>
   <MaxEnvelopes>1</MaxEnvelopes>
  </cwmp:InformResponse>
 </soapenv:Body>
</soapenv:Envelope>"""


def download_rpc(cwmp_id, command_key, url, size, target):
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"
                  xmlns:cwmp="urn:dslforum-org:cwmp-1-0">
 <soapenv:Header>
  <cwmp:ID soapenv:mustUnderstand="1">{cwmp_id}</cwmp:ID>
  <cwmp:HoldRequests soapenv:mustUnderstand="1">0</cwmp:HoldRequests>
 </soapenv:Header>
 <soapenv:Body>
  <cwmp:Download>
   <CommandKey>{command_key}</CommandKey>
   <FileType>1 Firmware Upgrade Image</FileType>
   <URL>{url}</URL>
   <Username></Username>
   <Password></Password>
   <FileSize>{size}</FileSize>
   <TargetFileName>{target}</TargetFileName>
   <DelaySeconds>0</DelaySeconds>
   <SuccessURL></SuccessURL>
   <FailureURL></FailureURL>
  </cwmp:Download>
 </soapenv:Body>
</soapenv:Envelope>"""


def transfer_complete_response(cwmp_id):
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"
                  xmlns:cwmp="urn:dslforum-org:cwmp-1-0">
 <soapenv:Header>
  <cwmp:ID soapenv:mustUnderstand="1">{cwmp_id}</cwmp:ID>
 </soapenv:Header>
 <soapenv:Body>
  <cwmp:TransferCompleteResponse/>
 </soapenv:Body>
</soapenv:Envelope>"""


ID_RE = re.compile(r"<cwmp:ID[^>]*>(.*?)</cwmp:ID>", re.DOTALL)
SERIAL_RE = re.compile(r"<SerialNumber>(.*?)</SerialNumber>", re.DOTALL)
EVENT_RE = re.compile(r"<EventCode>(.*?)</EventCode>", re.DOTALL)
SW_RE = re.compile(r"<Name>Device\.DeviceInfo\.SoftwareVersion</Name>\s*<Value[^>]*>(.*?)</Value>", re.DOTALL)
BODY_METHOD_RE = re.compile(r"<cwmp:(\w+)>", re.DOTALL)
FAULT_RE = re.compile(r"<FaultCode>(.*?)</FaultCode>", re.DOTALL)


class CWMPHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    OPTS = None

    def log_message(self, fmt, *args):
        log.info("%s - %s", self.client_address[0], fmt % args)

    def _unauthorized(self):
        self.send_response(401)
        self.send_header("WWW-Authenticate", 'Basic realm="CWMP"')
        self.send_header("Content-Length", "0")
        self.send_header("Connection", "close")
        self.end_headers()

    def _check_auth(self):
        """Accept any Basic creds unless --auth is explicitly set."""
        if not self.OPTS.auth or self.OPTS.auth.strip() == "":
            return True
        hdr = self.headers.get("Authorization", "")
        if not hdr.startswith("Basic "):
            return False
        try:
            raw = base64.b64decode(hdr.split(" ", 1)[1]).decode("latin-1")
        except Exception:
            return False
        return raw == self.OPTS.auth

    def _session(self):
        ip = self.client_address[0]
        with STATE_LOCK:
            s = STATE["sessions"].setdefault(ip, {"stage": "AWAITING_INFORM", "cwmp_id": None})
            STATE["last_cpe_ip"] = ip
        return ip, s

    def _which_ota(self):
        """Return (url, size, target) based on how many TransferComplete we have seen."""
        with STATE_LOCK:
            tc_count = STATE["transfer_complete_count"]
        if tc_count == 0:
            return (self.OPTS.pivot_url, self.OPTS.pivot_size, "pivot_ota_rg3205w_home.zip")
        else:
            if not self.OPTS.home_url:
                return None
            return (self.OPTS.home_url, self.OPTS.home_size, "Home-ota-rg3205w.zip")

    def do_POST(self):
        if not self._check_auth():
            log.warning("401 from %s", self.client_address[0])
            self._unauthorized()
            return

        ip, session = self._session()
        clen = int(self.headers.get("Content-Length", "0") or "0")
        body = self.rfile.read(clen) if clen > 0 else b""
        body_s = body.decode("utf-8", errors="replace")

        id_m = ID_RE.search(body_s)
        cwmp_id = id_m.group(1).strip() if id_m else "fake-ack"
        method_m = BODY_METHOD_RE.search(body_s)
        method = method_m.group(1) if method_m else ("Empty" if clen == 0 else "Unknown")

        log.info("← %s %s id=%s stage=%s (len=%d)", ip, method, cwmp_id, session["stage"], clen)

        # --- Inform ---
        if method == "Inform":
            serial_m = SERIAL_RE.search(body_s)
            event_m = EVENT_RE.search(body_s)
            sw_m = SW_RE.search(body_s)
            if serial_m:
                STATE["last_cpe_serial"] = serial_m.group(1).strip()
            event = (event_m.group(1).strip() if event_m else "?")
            sw = (sw_m.group(1).strip() if sw_m else "?")
            log.info("    serial=%s event=%s sw=%s", STATE["last_cpe_serial"], event, sw)
            session["stage"] = "INFORM_SEEN"
            session["cwmp_id"] = cwmp_id
            self._reply(200, inform_response(cwmp_id).encode("utf-8"))
            return

        # --- Empty / Fault / else ---
        if method == "Empty" or clen == 0:
            if session["stage"] == "INFORM_SEEN":
                ota = self._which_ota()
                if ota is None:
                    log.info("    all OTAs already served to this CPE; ending session")
                    session["stage"] = "DONE"
                    self._reply(204, b"")
                    return
                url, size, target = ota
                tc = STATE["transfer_complete_count"]
                phase = "PIVOT" if tc == 0 else "HOME-OTA"
                command_key = f"OTA-{phase}-{int(time.time())}"
                log.info("→ %s Download RPC url=%s target=%s commandKey=%s", phase, url, target, command_key)
                session["stage"] = "DOWNLOAD_SENT"
                self._reply(200, download_rpc(f"ACS_DL_{int(time.time())}",
                                               command_key, url, size, target).encode("utf-8"))
                with STATE_LOCK:
                    if phase == "PIVOT":
                        STATE["pivot_served_at"] = time.time()
                    else:
                        STATE["home_served_at"] = time.time()
                return
            else:
                log.info("    end session (stage=%s)", session["stage"])
                session["stage"] = "AWAITING_INFORM"
                self._reply(204, b"")
                return

        # --- TransferComplete (CPE reports OTA download done) ---
        if method == "TransferComplete":
            fault_m = FAULT_RE.search(body_s)
            fault = fault_m.group(1).strip() if fault_m else "0"
            log.info("    TransferComplete fault=%s (0 = OK)", fault)
            if fault == "0":
                with STATE_LOCK:
                    STATE["transfer_complete_count"] += 1
                    STATE["cycle"] += 1
                    log.info("    ★ TransferComplete count=%d; next OTA will be: %s",
                             STATE["transfer_complete_count"],
                             "HOME-OTA" if STATE["transfer_complete_count"] >= 1 and self.OPTS.home_url else "nothing")
            session["stage"] = "TRANSFER_COMPLETE"
            self._reply(200, transfer_complete_response(cwmp_id).encode("utf-8"))
            return

        # --- DownloadResponse (CPE accepted the Download RPC) ---
        if method == "DownloadResponse":
            log.info("    DownloadResponse (CPE accepted the Download RPC)")
            self._reply(204, b"")
            return

        log.info("    method=%s — ack empty", method)
        self._reply(204, b"")

    def do_GET(self):
        self.send_response(405)
        self.send_header("Content-Length", "0")
        self.send_header("Connection", "close")
        self.end_headers()

    def _reply(self, code, payload):
        self.send_response(code)
        if payload:
            self.send_header("Content-Type", "text/xml; charset=utf-8")
            self.send_header("Content-Length", str(len(payload)))
        else:
            self.send_header("Content-Length", "0")
        self.send_header("Connection", "keep-alive")
        self.end_headers()
        if payload:
            self.wfile.write(payload)


class ThreadingHTTPServer(ThreadingMixIn, HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--bind", default="0.0.0.0:7016")
    p.add_argument("--cert", help="server cert PEM (required unless --no-tls)")
    p.add_argument("--key", help="server key PEM (required unless --no-tls)")
    p.add_argument("--auth", default="",
                   help="expected Basic user:pass; empty = accept any (recommended for provisioned CPEs)")
    p.add_argument("--pivot-url", required=True,
                   help="URL for cycle-1 (pivot) Download RPC")
    p.add_argument("--pivot-size", type=int, default=31880617)
    p.add_argument("--home-url", default="",
                   help="URL for cycle-2 (Home 2) Download RPC; empty = skip cycle-2")
    p.add_argument("--home-size", type=int, default=0)
    p.add_argument("--no-tls", action="store_true", help="run plain HTTP (for port 7006)")
    opts = p.parse_args()

    host, port = opts.bind.rsplit(":", 1)
    port = int(port)
    CWMPHandler.OPTS = opts

    httpd = ThreadingHTTPServer((host, port), CWMPHandler)
    if not opts.no_tls:
        if not opts.cert or not opts.key:
            print("ERROR: --cert and --key required for TLS mode", file=sys.stderr)
            sys.exit(2)
        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        ctx.load_cert_chain(certfile=opts.cert, keyfile=opts.key)
        ctx.minimum_version = ssl.TLSVersion.TLSv1_2
        httpd.socket = ctx.wrap_socket(httpd.socket, server_side=True)

    scheme = "https" if not opts.no_tls else "http"
    log.info("CWMP server listening on %s://%s:%d/cwmpWeb/WGCPEMgt", scheme, host, port)
    log.info("Cycle 1 (pivot): url=%s size=%d", opts.pivot_url, opts.pivot_size)
    if opts.home_url:
        log.info("Cycle 2 (home-OTA): url=%s size=%d", opts.home_url, opts.home_size)
    else:
        log.info("Cycle 2 (home-OTA): DISABLED (--home-url not provided)")
    log.info("Auth: %s", "accept any" if not opts.auth else f"Basic {opts.auth}")

    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        log.info("bye")


if __name__ == "__main__":
    sys.exit(main())
