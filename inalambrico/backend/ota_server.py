#!/usr/bin/env python3
"""
ota_server.py — Plain HTTP file server for OTA zip downloads.

The Askey updater GETs the OTA with @Streaming over Authorization: Bearer <jwt>.
We don't verify the token (the device uses TLS trust-all anyway).

Usage:
  python3 ota_server.py --bind 0.0.0.0:8000 --root /srv/ota
"""
import argparse
import logging
import os
import sys
from http.server import SimpleHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn

logging.basicConfig(level=logging.INFO,
                    format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("ota")


class ThreadingHTTPServer(ThreadingMixIn, HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


class Handler(SimpleHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        log.info("%s - %s", self.client_address[0], fmt % args)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--bind", default="0.0.0.0:8000")
    p.add_argument("--root", default="./otas", help="dir with OTA zips")
    args = p.parse_args()

    os.chdir(args.root)
    host, port = args.bind.rsplit(":", 1)
    port = int(port)

    httpd = ThreadingHTTPServer((host, port), Handler)
    log.info("ota_server listening on http://%s:%d root=%s",
             host, port, os.path.abspath(args.root))
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        log.info("bye")


if __name__ == "__main__":
    sys.exit(main())
