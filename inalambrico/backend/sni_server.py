#!/usr/bin/env python3
"""
sni_server.py — Servidor TLS con SNI para el backend local del RG3205W.

Termina TLS en puerto 443 y selecciona certificado segun el SNI del dispositivo.
Serves JSON / HTML / image responses to all 77 Retrofit endpoints seen in the
token endpoint under api.es-pro.movistar-home.com.

Design: cherrypy-free. Pure stdlib. Threaded. Uses Python 3 standard library
only (ssl, http.server, socketserver, json, re).

Hosts handled (one cert each):
  main.acs.telefonica.net                       (CWMP is handled by cwmp_server.py
                                                 on port 7016 — this one answers only
                                                 the HTTPS probes to port 443)
  api.es-pro-movistar-home.com                  all /v1/* and /v2/* endpoints
  api.es-pro.movistar-home.com                  (variant, same handler)
  api.es-dev-movistar-home.com
  deviceresources-es-pro-movistar-home.s3.*.amazonaws.com   PNG/MP3 placeholders
  europe.directline.botframework.com            /conversations/*, /tokens/refresh
  auth.crt.nuance.com                           /oauth2/token/
  connectivitycheck.gstatic.com                 /generate_204

Usage:
  python3 sni_server.py --bind 0.0.0.0:443 --certs certs/ \
                        --ota-root http://10.42.0.1:8000
"""

import argparse
import base64
import glob
import hashlib
import json
import logging
import os
import re
import ssl
import sys
import time
import uuid
from datetime import datetime, timezone, timedelta
from http.server import BaseHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn
from urllib.parse import urlparse, parse_qs

from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives import padding as sym_padding

logging.basicConfig(level=logging.INFO,
                    format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("sni")


# One pixel PNG (89x89 bytes) used as placeholder for S3 assets
PNG_1x1 = base64.b64decode(
    b"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB"
    b"0C8AAAAASUVORK5CYII="
)
# Minimal fake MP3: ID3v2.4 empty header (10 bytes) + silent MP3 frame (32 bytes).
# Not a real audible track, but valid enough for players to accept.
MP3_SILENCE = (
    b"ID3\x04\x00\x00\x00\x00\x00\x00"   # ID3v2.4 header, 0 size
    b"\xff\xfb\x90\x00"                   # MP3 frame sync, MPEG1 L3 128kbps 44.1kHz
    + b"\x00" * 32                        # frame data
)

OPTS = None


FAKE_NOW = datetime(2025, 7, 1, 12, 0, 0, tzinfo=timezone.utc)


def iso_now():
    return FAKE_NOW.strftime("%Y-%m-%dT%H:%M:%S.000Z")


def iso_future(days):
    return (FAKE_NOW + timedelta(days=days)).strftime("%Y-%m-%dT%H:%M:%S.000Z")


def aes_encrypt_field(plaintext, secret, salt):
    """Encrypt a value matching the app's AesUtils.encrypt() scheme.

    AesUtils.decrypt() in la app stock hace:
      key  = PBKDF2WithHmacSHA256(secret=deviceId, salt=sub, iter=65536, 256-bit)
      data = Base64.decode(input, NO_WRAP)
      IV   = data[0:16]
      ct   = data[16:]
      AES/CBC/PKCS5Padding.decrypt(ct, key, IV)

    So we encrypt in reverse: pad → AES/CBC encrypt → prepend IV → base64.
    """
    key = hashlib.pbkdf2_hmac(
        "sha256",
        secret.encode("utf-8"),
        salt.encode("utf-8"),
        65536,
        dklen=32,
    )
    iv = os.urandom(16)
    padder = sym_padding.PKCS7(128).padder()
    padded = padder.update(plaintext.encode("utf-8")) + padder.finalize()
    enc = Cipher(algorithms.AES(key), modes.CBC(iv)).encryptor()
    ct = enc.update(padded) + enc.finalize()
    return base64.b64encode(iv + ct).decode("utf-8")


def fake_jwt(device_id="askey-device"):
    """Return a JWT with AES-encrypted admin_code and phone_number.

    The app's saveJwt() calls JWTMapper.unwrapJWT() then storePayload() which
    runs AesUtils.decrypt() on admin_code and phone_number.  If those fields
    are plaintext, decrypt() throws RuntimeException → HttpServerError →
    ON.100.014.  We encrypt them here with the same PBKDF2+AES-CBC scheme.

    JWTMapper uses Base64.decode(payload, 8) where 8 = URL_SAFE, so the JWT
    parts use base64url encoding (RFC 4648 §5).
    """
    import hmac
    sub = device_id

    enc_phone = aes_encrypt_field("+34600000000", device_id, sub)
    enc_admin = aes_encrypt_field("ADMIN", device_id, sub)

    header = {"alg": "HS256", "typ": "JWT"}
    fake_now = int(datetime(2025, 7, 1, 12, 0, 0, tzinfo=timezone.utc).timestamp())
    payload = {
        "sub":            sub,
        "iat":            fake_now,
        "exp":            fake_now + 60 * 60 * 24 * 365 * 10,
        "admin_code":     enc_admin,
        "phone_number":   enc_phone,
        "mobile_phones":  [],
        "external_urls":  {
            "aura_token":     "https://api.es-pro.movistar-home.com/aura/token",
            "iptv_homezone":  "https://hbmovistar.imagenio.telefonica.net",
        },
    }
    b64url = lambda d: base64.urlsafe_b64encode(d).rstrip(b"=").decode()
    h = b64url(json.dumps(header, separators=(",", ":")).encode("utf-8"))
    p = b64url(json.dumps(payload, separators=(",", ":")).encode("utf-8"))
    sig = hmac.new(b"fake-secret", f"{h}.{p}".encode(), hashlib.sha256).digest()
    s = b64url(sig)
    return f"{h}.{p}.{s}"


# /v1/discovery/{version} devuelve Single<List<GroupResponse>> (array JSON, Moshi).
# /v1/features/{version} devuelve Single<List<FeatureResponse>> (array JSON).
# Un array vacio es una List valida y vacia: no falla la deserializacion ni
# produce NPE (lista vacia, no null). Es contenido del launcher, no del wizard,
# asi que vacio es seguro y suficiente.
def discovery_body(host):
    return []


def features_body():
    return []


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def version_string(self):
        return "nginx"

    def date_time_string(self, timestamp=None):
        # Return date matching NTP time (July 2025)
        # so OkHttp doesn't see a response from the "future"
        return FAKE_NOW.strftime("%a, %d %b %Y %H:%M:%S GMT")

    def log_message(self, fmt, *args):
        host = self.headers.get("Host", "?")
        log.info("%s %s - %s", host, self.client_address[0], fmt % args)

    def send_json(self, obj, code=200):
        payload = json.dumps(obj).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Connection", "keep-alive")
        self.end_headers()
        self.wfile.write(payload)

    def send_text(self, text, code=200, ctype="text/plain; charset=utf-8"):
        payload = text.encode("utf-8") if isinstance(text, str) else text
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Connection", "keep-alive")
        self.end_headers()
        self.wfile.write(payload)

    def send_bytes(self, blob, ctype):
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(blob)))
        self.send_header("Connection", "keep-alive")
        self.end_headers()
        self.wfile.write(blob)

    def send_file_streaming(self, fpath, ctype):
        fsize = os.path.getsize(fpath)
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(fsize))
        self.send_header("Connection", "close")
        self.end_headers()
        sent = 0
        t0 = time.time()
        fname = os.path.basename(fpath)
        prog_path = "/opt/mh-fake/otas/.dl_progress.json"

        def _prog(active):
            # Escribe el progreso de descarga para que el panel lo muestre.
            try:
                import json as _j
                tmp = prog_path + ".tmp"
                with open(tmp, "w") as pf:
                    _j.dump({"file": fname, "sent": sent, "total": fsize,
                             "started": t0, "updated": time.time(),
                             "active": active}, pf)
                os.replace(tmp, prog_path)
            except Exception:
                pass

        _last = 0.0
        try:
            _prog(True)
            with open(fpath, "rb") as f:
                while True:
                    chunk = f.read(65536)
                    if not chunk:
                        break
                    self.wfile.write(chunk)
                    sent += len(chunk)
                    now = time.time()
                    if now - _last >= 0.4:
                        _prog(True)
                        _last = now
            self.wfile.flush()
            _prog(False)
            elapsed = time.time() - t0
            log.info(">>> DOWNLOAD COMPLETE: %s — %d/%d bytes en %.2fs (%.1f Mbps)",
                     fname, sent, fsize, elapsed,
                     (sent * 8 / elapsed / 1e6) if elapsed > 0 else 0)
        except (BrokenPipeError, ConnectionResetError, OSError) as e:
            _prog(False)
            elapsed = time.time() - t0
            log.warning(">>> DOWNLOAD ABORTED: %s — %d/%d bytes en %.2fs — %s",
                        fname, sent, fsize, elapsed, e)


    def send_empty(self, code=204):
        self.send_response(code)
        self.send_header("Content-Length", "0")
        self.send_header("Connection", "keep-alive")
        self.end_headers()

    # ---------- route dispatch ----------
    def _route(self):
        host = (self.headers.get("Host") or "").split(":")[0].lower()
        parsed = urlparse(self.path)
        p = parsed.path
        q = parse_qs(parsed.query)
        return host, p, q

    def do_GET(self):
        host, p, q = self._route()
        self._dispatch("GET", host, p, q)

    def _drain_body(self):
        cl = int(self.headers.get("Content-Length", 0))
        self._body = self.rfile.read(cl) if cl > 0 else b""

    def do_POST(self):
        self._drain_body()
        host, p, q = self._route()
        self._dispatch("POST", host, p, q)

    def do_PUT(self):
        self._drain_body()
        host, p, q = self._route()
        self._dispatch("PUT", host, p, q)

    def do_DELETE(self):
        self._drain_body()
        host, p, q = self._route()
        self._dispatch("DELETE", host, p, q)

    def _dispatch(self, method, host, path, query):
        # ---- captive portal ----
        if host == "connectivitycheck.gstatic.com" or path == "/generate_204":
            self.send_response(204)
            self.send_header("Content-Length", "0")
            self.send_header("Connection", "keep-alive")
            self.end_headers()
            return

        # ---- S3 buckets: images / sounds / html ----
        if "s3." in host and "amazonaws.com" in host:
            if path.endswith(".png") or path.endswith(".jpg") or path.endswith(".webp"):
                return self.send_bytes(PNG_1x1, "image/png")
            if path.endswith(".mp3"):
                return self.send_bytes(MP3_SILENCE, "audio/mpeg")
            if path.endswith(".html"):
                return self.send_text(
                    "<!doctype html><title>MH error</title><body>"
                    "Un error inesperado. Reintente.</body>",
                    ctype="text/html; charset=utf-8")
            return self.send_bytes(PNG_1x1, "image/png")

        # ---- Nuance OAuth ----
        if host.endswith("nuance.com") and path.rstrip("/") == "/oauth2/token":
            return self.send_json({
                "access_token": "nuance-fake-" + uuid.uuid4().hex,
                "token_type": "Bearer",
                "expires_in": 3600,
                "scope": "asr tts",
            })

        # ---- DirectLine ----
        if host.endswith("directline.botframework.com"):
            if path == "/v3/directline/tokens/refresh" and method == "POST":
                return self.send_json({
                    "token": "directline-fake-" + uuid.uuid4().hex,
                    "expires_in": 1800
                })
            if path == "/v3/directline/conversations" and method == "POST":
                cid = uuid.uuid4().hex
                return self.send_json({
                    "conversationId": cid,
                    "token": "directline-fake-" + uuid.uuid4().hex,
                    "streamUrl": "",
                    "expires_in": 1800
                })
            m = re.match(r"^/v3/directline/conversations/([^/]+)(?:/activities)?$", path)
            if m:
                if method == "POST":
                    return self.send_json({"id": uuid.uuid4().hex})
                return self.send_json({"activities": [], "watermark": "0"})
            return self.send_json({})

        # ---- Video de bienvenida (HLS en el blob de Azure) ----
        # VideoActivity reproduce un intro por HLS. Servimos el playlist y los
        # segmentos .ts desde video_dir para que ExoPlayer no falle.
        if host.endswith("blob.core.windows.net") or path.endswith((".m3u8", ".ts")):
            return self.serve_video(path)

        # ---- api.es-*-movistar-home.com + api.es-*.movistar-home.com ----
        return self._api_route(method, host, path, query)

    def serve_video(self, path):
        base = os.path.basename(urlparse(path).path)
        vdir = getattr(OPTS, "video_dir", "video")
        fp = os.path.join(vdir, base)
        if os.path.isfile(fp):
            if base.endswith(".m3u8"):
                ct = "application/vnd.apple.mpegurl"
            elif base.endswith(".ts"):
                ct = "video/mp2t"
            else:
                ct = "application/octet-stream"
            with open(fp, "rb") as f:
                return self.send_bytes(f.read(), ct)
        # Sin fichero: playlist VOD vacio valido (ExoPlayer no casca)
        if base.endswith(".m3u8"):
            return self.send_text(
                "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:1\n#EXT-X-ENDLIST\n",
                ctype="application/vnd.apple.mpegurl")
        return self.send_empty(404)

    # ------------------------------------------------------------------ #
    # API routes
    # ------------------------------------------------------------------ #
    def _api_route(self, method, host, path, query):
        # Normalize: strip leading /api if present
        if path.startswith("/api/"):
            path = path[4:]
        # Strip trailing slash except root
        if path != "/" and path.endswith("/"):
            path = path[:-1]

        # ---- Auth ----
        if path == "/v2/auth/jwt" and method == "GET":
            device = (query.get("device_id") or ["askey-rg3205w"])[0]
            log.info(">>> AUTH REQUEST from %s device_id=%s", self.client_address[0], device)
            for hdr, val in self.headers.items():
                log.info("    REQ HDR: %s: %s", hdr, val)
            jwt_token = fake_jwt(device)
            body = {"jwt": jwt_token}
            payload = json.dumps(body, separators=(",", ":")).encode("utf-8")
            log.info("    RESP: 200, jwt length=%d, body bytes=%d",
                     len(jwt_token), len(payload))
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.send_header("X-Correlator", str(uuid.uuid4()))
            self.send_header("Connection", "close")
            self.end_headers()
            self.wfile.write(payload)
            self.wfile.flush()
            return

        if path.startswith("/v1/auth/"):
            # aura_id, mc/{msisdn}, msasr, nuance, zoom, directline_token
            if path == "/v1/auth/aura_id":
                return self.send_json({"aura_id": "aura-fake-001"})
            if path == "/v1/auth/msasr":
                return self.send_json({"token": "msasr-fake", "endpoint": "wss://invalid.local"})
            if path == "/v1/auth/nuance":
                return self.send_json({
                    "access_token": "nuance-fake-" + uuid.uuid4().hex,
                    "token_type": "Bearer",
                    "expires_in": 3600,
                })
            if path == "/v1/auth/zoom":
                return self.send_json({"token": "zoom-fake", "sdk_key": "fake"})
            if path.startswith("/v1/auth/mc/"):
                return self.send_json({"status": "OK"})
            if path == "/v1/auth/directline_token/" or path == "/v1/auth/directline_token":
                return self.send_json({
                    "token": "directline-fake-" + uuid.uuid4().hex,
                    "conversationId": uuid.uuid4().hex,
                    "expires_in": 1800,
                    "streamUrl": ""
                })

        # ---- Config / Discovery / Features / Suggestions ----
        if path == "/v1/config":
            return self.send_json({
                "radio.carousel.size": "10",
                "grammars.local.enabled": "true",
                "bulletins.json": "[]",
                "terms.aura.version": "1",
                "terms.movistar_home.version": "1",
                "unknown_error.threshold": "5",
                "livingapp.enabled": "true",
                "football.enabled": "true",
                "relaxing_sounds.enabled": "true",
                "routines.enabled": "true",
                "comms.enabled": "true",
                "aura.enabled": "true",
                "copies.generic_error": "Error, intente de nuevo",
                "urls.privacy": f"https://{host}/privacy",
                "urls.terms": f"https://{host}/terms",
            })
        if path.startswith("/v1/discovery/"):
            return self.send_json(discovery_body(host))
        if path.startswith("/v1/features/"):
            return self.send_json(features_body())
        if path.startswith("/v1/suggestions/"):
            return self.send_json({"suggestions": []})

        # ---- Updates ----
        if path.startswith("/v1/updates/"):
            if method == "GET":
                if getattr(OPTS, "no_update", False):
                    return self.send_json({"hasUpdate": False, "url": ""})
                pivot = getattr(OPTS, "pivot_file", "pivot_ota_rg3205w_home.zip")
                pivot_path = os.path.join("/opt/mh-fake/otas", pivot)
                if not os.path.isfile(pivot_path):
                    log.warning("OTA pivot no encontrado: %s — respondiendo sin update", pivot_path)
                    return self.send_json({"hasUpdate": False, "url": ""})
                pivot_size = os.path.getsize(pivot_path)
                if pivot_size < 1000:
                    log.warning("OTA pivot vacio o corrupto (%d bytes): %s", pivot_size, pivot_path)
                    return self.send_json({"hasUpdate": False, "url": ""})
                return self.send_json({
                    "hasUpdate": True,
                    "url": f"https://api.es-pro.movistar-home.com/api/ota/{pivot}",
                    "version": "99.8.0",
                    "fw": "eng.sw3.20261111.010000",
                    "size": pivot_size,
                    "description": "System update",
                    "mandatory": True
                })

        # ---- OTA file download (HTTPS) ----
        if path.startswith("/ota/") and method == "GET":
            fname = path[5:]
            fpath = os.path.join("/opt/mh-fake/otas", fname)
            if os.path.exists(fpath):
                log.info(">>> SERVING OTA via HTTPS (streaming): %s (%d bytes)", fpath, os.path.getsize(fpath))
                return self.send_file_streaming(fpath, "application/zip")
            else:
                log.warning(">>> OTA NOT FOUND: %s", fpath)
                return self.send_json({"error": "not found"}, code=404)

        if path == "/apps" and method == "GET":
            return self.send_json({"apps": []})

        # ---- Aura voz (TTS): /v1/voice?msg=... devuelve audio ----
        # Las pantallas de Terminos reproducen la voz de Aura. El modelo del
        # aparato espera un stream de audio (ResponseBody), no JSON: devolvemos
        # un MP3 silencioso valido para que el reproductor no falle.
        if path == "/v1/voice" or path.startswith("/v1/voice/"):
            return self.send_bytes(MP3_SILENCE, "audio/mpeg")

        # ---- Users ----
        if path == "/v1/users":
            return self.send_json({
                "total": 1, "next": None, "previous": None,
                "results": [{"created": iso_now(), "phone": "+34600000000"}],
            })
        if path.startswith("/v1/users/me/hgu/devices/info/"):
            dev = path.rsplit("/", 1)[-1]
            return self.send_json({
                "device_id": dev, "mac": "00:00:00:00:00:00",
                "hostname": "RG3205W", "online": True, "rssi": -45,
            })
        if path.startswith("/v2/users/me/hgu/devices/rssi/"):
            return self.send_json({"rssi": -45})
        if path == "/v2/users/me/router/device-map":
            return self.send_json({"devices": [{
                "device_id": "askey-rg3205w-01",
                "mac": "00:00:00:00:00:00",
                "hostname": "RG3205W",
                "online": True, "rssi": -45,
            }]})
        if path == "/v1/users/me/router/compatibility/haac":
            return self.send_json({"compatible": True})
        if path == "/v1/users/me/weather":
            return self.send_json({"temperature": 20, "humidity": 50, "condition": "clear"})
        if path == "/v1/users/me/weather/forecast":
            return self.send_json({"forecast": []})
        if path == "/v1/users/me/weather/towns":
            return self.send_json({"towns": [{"id": 1, "name": "Madrid"}]})
        if path.startswith("/v1/users/me/events/") and method == "PUT":
            body = getattr(self, "_body", b"")
            log.info(">>> EVENT PUT %s body=%s", path, body[:4000].decode("utf-8", errors="replace") if body else "(empty)")
            return self.send_empty(200)
        if path == "/v1/users/me/iptv":
            return self.send_json({
                "iptvDevices": [],
                "demarcation": "28",
                "purchases": [],
                "subscriptions": [],
            })
        if path.startswith("/v1/users/me/iptv-contents-bookmarks"):
            return self.send_json({"bookmarks": []})
        if path.startswith("/v1/users/me/iptv-series-tracking/"):
            return self.send_json({"series": []})

        # ---- Wishlist ----
        if path == "/v2/users/me/wishlist":
            return self.send_json({"items": []})
        if path.startswith("/v2/users/me/wishlist/") and method in ("PUT", "DELETE"):
            return self.send_empty(200)

        # ---- Radio / Podcasts ----
        if path.startswith("/v1/radio/"):
            return self.send_json({"items": []})

        # ---- Music ----
        if path.startswith("/v1/music/"):
            if method == "POST":
                return self.send_json({"url": "", "stream": ""})
            return self.send_json({"items": []})

        # ---- Notifications (specific endpoints BEFORE generic) ----
        if path == "/v1/notifications/connect_string" and method == "POST":
            return self.send_json({
                "connect_string": "",
                "connectionString": "",
                "notificationHubPath": "",
                "status": "disabled"
            })
        if path == "/v1/notifications/register" and method == "POST":
            return self.send_json({
                "registrationId": "fake-reg-" + uuid.uuid4().hex,
                "status": "ok"
            })

        # ---- News / Football / Games / Habits / LivingApps / Grammars /
        #      Recomms / Notifications / Smart Gaming / Special-contacts /
        #      Contents / Resources / Zoom / Ads
        for prefix in ("/v1/news", "/v1/football", "/v2/games", "/v1/habits",
                       "/v1/living_apps", "/v1/grammars", "/v1/recomms",
                       "/v1/notifications", "/v1/smartgaming", "/v1/special-contacts",
                       "/v1/contents", "/v2/contents", "/v2/resources",
                       "/v1/zoom", "/v2/users/me/wishlist", "/v2/users/me/router"):
            if path.startswith(prefix):
                if method == "POST":
                    return self.send_json({"status": "ok"})
                return self.send_json({"items": [], "total": 0, "results": []})

        # ---- HomeZone (asmgr) ----
        if path.startswith("/asmgr/ccs/getHZTokenOnHome/"):
            return self.send_json({
                "resultCode": 0,
                "resultData": {"homeZoneID": "hz-fake-" + uuid.uuid4().hex},
                "resultText": "OK",
            })

        # ---- Aura token (external_urls.aura_token from JWT) ----
        if path == "/aura/token":
            return self.send_json({
                "access_token": "aura-fake-" + uuid.uuid4().hex,
                "token_type": "Bearer",
                "expires_in": 3600,
            })

        # ---- Ads / Banners ----
        if path.startswith("/v1/ads") or path.startswith("/v2/ads"):
            return self.send_json({"ads": [], "banners": []})

        # ---- Fallbacks ----
        if path == "/privacy" or path == "/terms":
            return self.send_text(
                "<!doctype html><title>MH</title><body><h1>Terms</h1><p>Fake.</p></body>",
                ctype="text/html; charset=utf-8")

        log.warning("UNMATCHED %s https://%s%s", method, host, self.path)
        for hdr, val in self.headers.items():
            log.warning("    UNMATCHED HDR: %s: %s", hdr, val)
        return self.send_json({"status": "ok"}, code=200)


class ThreadingHTTPServer(ThreadingMixIn, HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


def build_sni_context(certs_dir):
    """Build a default context + per-host contexts via sni_callback."""
    # Pick any cert as default (will be overridden by SNI callback)
    certs = {}
    for crt in glob.glob(os.path.join(certs_dir, "*.crt")):
        name = os.path.basename(crt)[:-4]
        if name in ("root",):
            continue
        key = os.path.join(certs_dir, name + ".key")
        if not os.path.exists(key):
            continue
        certs[name] = (crt, key)
    if not certs:
        log.error("No certs found in %s", certs_dir)
        sys.exit(2)

    # Default = first in sorted order
    default_name = sorted(certs.keys())[0]

    def mkctx(crt, key):
        c = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        c.load_cert_chain(certfile=crt, keyfile=key)
        c.minimum_version = ssl.TLSVersion.TLSv1_2
        return c

    host_ctx = {name: mkctx(c, k) for name, (c, k) in certs.items()}
    default_ctx = host_ctx[default_name]

    def sni_cb(sslsock, server_name, ctx):
        if not server_name:
            return
        for cert_name, cctx in host_ctx.items():
            if server_name == cert_name or server_name.endswith("." + cert_name.split(".", 1)[-1]):
                sslsock.context = cctx
                return
        # Try prefix match on full cert_name
        for cert_name, cctx in host_ctx.items():
            if cert_name in server_name or server_name in cert_name:
                sslsock.context = cctx
                return

    default_ctx.sni_callback = sni_cb
    log.info("SNI: default=%s | hosts=%s", default_name, ", ".join(sorted(host_ctx)))
    return default_ctx


def main():
    global OPTS
    p = argparse.ArgumentParser()
    p.add_argument("--bind", default="0.0.0.0:443")
    p.add_argument("--certs", required=True, help="dir with *.crt and *.key")
    p.add_argument("--ota-root", default="http://10.42.0.1:8000",
                   help="HTTP root for OTA downloads")
    p.add_argument("--pivot-file", default="pivot_ota_rg3205w_home.zip",
                   help="nombre del archivo pivot en /opt/mh-fake/otas/")
    p.add_argument("--pivot-size", type=int, default=33844257,
                   help="tamano en bytes del pivot (fallback, se autodetecta)")
    p.add_argument("--no-update", action="store_true",
                   help="onboarding-only: /v1/updates/fw returns hasUpdate:false")
    p.add_argument("--video-dir", default="video",
                   help="dir con el HLS del video de bienvenida (.m3u8/.ts)")
    OPTS = p.parse_args()

    host, port = OPTS.bind.rsplit(":", 1)
    port = int(port)

    ctx = build_sni_context(OPTS.certs)
    httpd = ThreadingHTTPServer((host, port), Handler)
    httpd.socket = ctx.wrap_socket(httpd.socket, server_side=True)
    log.info("sni_server listening on https://%s:%d", host, port)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        log.info("bye")


if __name__ == "__main__":
    sys.exit(main())
