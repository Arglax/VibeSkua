#!/usr/bin/env python3
"""
Local server for the MoVibeSkua usage dashboard.

    python tools/dashboard/serve_dashboard.py
    python tools/dashboard/serve_dashboard.py --interval 15 --no-browser --port 8768

Serves tools/dashboard/ on http://localhost:8768, runs tools/aggregate_usage.py once the port is
bound and then every --interval minutes (0 = only when you press Refresh), and handles POST /refresh
for the dashboard's Refresh button. Binds to localhost only.
"""
import argparse
import http.server
import os
import subprocess
import sys
import threading
import webbrowser

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
AGGREGATE = os.path.join(ROOT, "tools", "aggregate_usage.py")
DEFAULT_PORT = 8768

lock = threading.Lock()
aggregate_args = []


def run_aggregate():
    with lock:
        result = subprocess.run([sys.executable, AGGREGATE] + aggregate_args, capture_output=True, text=True)
        print((result.stdout or result.stderr).strip(), flush=True)
        return result.returncode == 0


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=HERE, **kwargs)

    def do_POST(self):
        if self.path != "/refresh":
            self.send_error(404)
            return
        ok = run_aggregate()
        self.send_response(200 if ok else 500)
        self.end_headers()
        self.wfile.write(b"ok" if ok else b"failed")

    def log_message(self, *args):
        pass


def schedule(minutes):
    if minutes <= 0:
        return
    timer = threading.Timer(minutes * 60, lambda: (run_aggregate(), schedule(minutes)))
    timer.daemon = True
    timer.start()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--interval", type=int, default=30)
    parser.add_argument("--key", help="service account key, passed to aggregate_usage.py")
    parser.add_argument("--no-browser", action="store_true")
    args = parser.parse_args()
    if args.key:
        aggregate_args.extend(["--key", args.key])

    server = http.server.ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    url = "http://localhost:" + str(args.port) + "/index.html"
    threading.Thread(target=lambda: (run_aggregate(), schedule(args.interval)), daemon=True).start()
    print("dashboard: " + url, flush=True)
    if not args.no_browser:
        webbrowser.open(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
