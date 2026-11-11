#!/usr/bin/env python3
"""Run synthetic localhost streaming/HLS and system integration tests; no IPTV account."""
import http.server
import os
from pathlib import Path
import subprocess
import sys
import threading
import time

root = Path(__file__).resolve().parents[1]
fixture = Path(os.environ.get("TELEPILOT_TEST_TS_FILE", "/tmp/telepilot-buffer-fixture.ts"))
if not fixture.is_file():
    sys.exit("Generate a 15-second synthetic MPEG-TS at TELEPILOT_TEST_TS_FILE first.")
content = fixture.read_bytes()


class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        try:
            if self.path in ["/redirect/master.m3u8", "/redirect/playlist.m3u"]:
                target = "/cdn/" + self.path.rsplit("/", 1)[1]
                self.send_response(302)
                self.send_header("Location", target)
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
            if self.path == "/cdn/playlist.m3u":
                body = b'#EXTM3U x-tvg-url="guide.xml"\n#EXTINF:-1,Channel\nstreams/channel.ts\n'
                self.send_response(200)
                self.send_header("Content-Type", "audio/x-mpegurl")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
                return
            if self.path.endswith(".m3u8"):
                if self.path == "/slow.m3u8":
                    time.sleep(1.5)
                if "Chrome/118" not in self.headers.get("User-Agent", ""):
                    self.send_error(403)
                    return
                body = b"#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=5000000\nhigh.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=2000000\nlow.m3u8\n"
                self.send_response(200)
                self.send_header("Content-Type", "application/vnd.apple.mpegurl")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
                return
            if self.path == "/failed.ts":
                self.send_error(503)
                return
            if self.path == "/finite.ts":
                # Three seconds of this generated TS, served as a finite input.
                body = content[:max(188, (len(content) // 5 // 188) * 188)]
                self.send_response(200)
                self.send_header("Content-Type", "video/mp2t")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
                return
            self.send_response(200)
            self.send_header("Content-Type", "video/mp2t")
            self.end_headers()
            # A real-time producer. Fast-start cannot rely on downloading a whole file.
            chunk = 188 * 40
            start = time.monotonic()
            for offset in range(0, len(content), chunk):
                self.wfile.write(content[offset:offset + chunk])
                self.wfile.flush()
                delay = start + (offset + chunk) / len(content) * 15 - time.monotonic()
                if delay > 0:
                    time.sleep(delay)
        except (BrokenPipeError, ConnectionResetError):
            pass


server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
server.daemon_threads = True
threading.Thread(target=server.serve_forever, daemon=True).start()
base = f"http://127.0.0.1:{server.server_port}"
environment = dict(os.environ, TELEPILOT_RUN_PLAYBACK_INTEGRATION="1", TELEPILOT_TEST_HTTP_BASE=base,
                   TELEPILOT_TEST_STREAM_URL=base + "/network.ts")
try:
    result = subprocess.run([str(root / "scripts/test.sh"), *sys.argv[1:]], env=environment)
finally:
    server.shutdown()
    server.server_close()
sys.exit(result.returncode)
