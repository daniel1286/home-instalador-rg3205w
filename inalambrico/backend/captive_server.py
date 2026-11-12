#!/usr/bin/env python3
"""
captive_server.py — HTTP server on port 80 for Android captive portal check.

Android 8.1 sends a HTTP GET (NOT HTTPS) to these URLs when a WiFi
network comes up, to decide if the network has internet. If it gets 204,
Android marks the network as NET_CAPABILITY_VALIDATED. If it does not,
Android shows "no internet" in notifications and apps' isInternetAvailable()
returns false → launcher opens NetworkConnectivityErrorActivity.

Default URLs (hardcoded in frameworks/base NetworkMonitor):
  http://connectivitycheck.gstatic.com/generate_204
  http://clients3.google.com/generate_204

Our dnsmasq already redirects these hostnames to the Pi (10.42.0.1).
This server listens on :80 and returns 204 for any /generate_204 request.
"""

import argparse
import logging
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn

logging.basicConfig(level=logging.INFO,
                    format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("captive")


class ThreadingHTTPServer(ThreadingMixIn, HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        host = self.headers.get("Host", "?")
        log.info("%s %s - %s", host, self.client_address[0], fmt % args)

    def do_GET(self):
        # Any URL ending in /generate_204 (or just that path) returns 204
        if "generate_204" in self.path or self.path == "/":
            self.send_response(204)
            self.send_header("Content-Length", "0")
            self.send_header("Connection", "keep-alive")
            self.end_headers()
            return
        # Anything else — same, just in case
        self.send_response(204)
        self.send_header("Content-Length", "0")
        self.send_header("Connection", "keep-alive")
        self.end_headers()

    def do_HEAD(self):
        self.do_GET()


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--bind", default="0.0.0.0:80")
    args = p.parse_args()
    host, port = args.bind.rsplit(":", 1)
    port = int(port)
    httpd = ThreadingHTTPServer((host, port), Handler)
    log.info("captive_server listening on http://%s:%d (returns 204 for everything)", host, port)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        log.info("bye")


if __name__ == "__main__":
    sys.exit(main())
