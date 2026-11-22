"""
Break Out leaderboard - zero dependency version (Python 3 standard library only).

Usage:
    LB_SECRET=your-secret PORT=8000 python3 leaderboard_min.py

Endpoints:
    GET  /health
    GET  /scores?limit=10
    POST /scores   {"name","score","wave","level","kills","checksum"}

checksum = sha256(f"{name}|{score}|{wave}|{kills}|{SECRET}") hex
"""
import hashlib
import json
import os
import sqlite3
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

SECRET = os.environ.get("LB_SECRET", "change-me")
DB_PATH = os.environ.get("LB_DB", "leaderboard.db")
PORT = int(os.environ.get("PORT", "8000"))

_recent = {}
RATE_WINDOW = 60.0
RATE_LIMIT = 5


def db():
    conn = sqlite3.connect(DB_PATH)
    conn.execute(
        "CREATE TABLE IF NOT EXISTS scores ("
        "id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, "
        "score INTEGER NOT NULL, wave INTEGER NOT NULL, level INTEGER NOT NULL, "
        "kills INTEGER NOT NULL, created_at TEXT NOT NULL, ip TEXT)"
    )
    return conn


def checksum(name, score, wave, kills):
    raw = "%s|%d|%d|%d|%s" % (name, score, wave, kills, SECRET)
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def _send(self, code, obj):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, fmt, *args):
        pass

    def do_GET(self):
        url = urlparse(self.path)
        if url.path == "/health":
            return self._send(200, {"ok": True, "time": time.time()})
        if url.path == "/scores":
            limit = 10
            qs = parse_qs(url.query)
            if "limit" in qs:
                try:
                    limit = max(1, min(int(qs["limit"][0]), 50))
                except ValueError:
                    limit = 10
            conn = db()
            rows = conn.execute(
                "SELECT name, score, wave, level, kills, created_at FROM scores "
                "ORDER BY score DESC, id ASC LIMIT ?", (limit,)
            ).fetchall()
            conn.close()
            items = [dict(zip(("name", "score", "wave", "level", "kills", "created_at"), r)) for r in rows]
            return self._send(200, {"items": items})
        return self._send(404, {"detail": "not found"})

    def do_POST(self):
        if urlparse(self.path).path != "/scores":
            return self._send(404, {"detail": "not found"})

        length = int(self.headers.get("Content-Length", "0"))
        try:
            data = json.loads(self.rfile.read(length).decode("utf-8"))
        except Exception:
            return self._send(400, {"detail": "bad json"})

        name = (data.get("name") or "").strip()[:16] or "anon"
        score = int(data.get("score", -1))
        wave = int(data.get("wave", 0))
        level = int(data.get("level", 0))
        kills = int(data.get("kills", 0))
        sig = (data.get("checksum") or "").lower()

        if sig != checksum(name, score, wave, kills):
            return self._send(400, {"detail": "checksum mismatch"})
        if not (0 <= score <= 10000000):
            return self._send(400, {"detail": "score out of range"})
        if not (1 <= wave <= 9999):
            return self._send(400, {"detail": "wave out of range"})
        if not (0 <= kills <= 100000) or score > max(100, kills * 500):
            return self._send(400, {"detail": "score/kills mismatch"})

        ip = self.client_address[0]
        now = time.time()
        stamps = [t for t in _recent.get(ip, []) if now - t < RATE_WINDOW]
        if len(stamps) >= RATE_LIMIT:
            return self._send(429, {"detail": "too many submissions"})
        stamps.append(now)
        _recent[ip] = stamps

        created = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime())
        conn = db()
        cur = conn.execute(
            "INSERT INTO scores(name, score, wave, level, kills, created_at, ip) VALUES(?,?,?,?,?,?,?)",
            (name, score, wave, level, kills, created, ip),
        )
        conn.commit()
        rank = conn.execute("SELECT COUNT(*) FROM scores WHERE score > ?", (score,)).fetchone()[0] + 1
        conn.close()
        return self._send(200, {"ok": True, "rank": rank, "id": cur.lastrowid})


if __name__ == "__main__":
    db().close()
    print("leaderboard listening on 0.0.0.0:%d  (db=%s)" % (PORT, DB_PATH), flush=True)
    print("secret loaded: %s..." % SECRET[:4], flush=True)
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
