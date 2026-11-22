"""
Break Out 联网排行榜 —— 最小可用服务端
依赖：fastapi, uvicorn  (见 requirements.txt)

启动：
    # Linux / macOS
    export LB_SECRET="换成你自己的随机串"
    uvicorn leaderboard_server:app --host 0.0.0.0 --port 8000

    # Windows
    set LB_SECRET=换成你自己的随机串
    uvicorn leaderboard_server:app --host 0.0.0.0 --port 8000

接口：
    GET  /health              健康检查
    GET  /scores?limit=10     排行榜前 N
    POST /scores              提交分数

注意：
    1. LB_SECRET 必须和 Unity 端 LeaderboardClient.Secret 一致
    2. 客户端提交的分数永远可以被伪造，这里的校验只能挡住「随手改分」，
       真要防作弊得服务端开局、上传回放或做行为校验
"""
import hashlib
import os
import sqlite3
import time
from contextlib import closing
from typing import Optional

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel

SECRET = os.environ.get("LB_SECRET", "change-me")
DB_PATH = os.environ.get("LB_DB", "leaderboard.db")

# 同一 IP 的提交频率限制（简单内存版，重启即清空）
RATE_WINDOW = 60.0      # 秒
RATE_LIMIT = 5          # 窗口内最多提交几次
_recent = {}

app = FastAPI(title="Break Out Leaderboard", version="1.0")


def db() -> sqlite3.Connection:
    conn = sqlite3.connect(DB_PATH)
    conn.execute(
        """
        CREATE TABLE IF NOT EXISTS scores (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            name       TEXT    NOT NULL,
            score      INTEGER NOT NULL,
            wave       INTEGER NOT NULL,
            level      INTEGER NOT NULL,
            kills      INTEGER NOT NULL,
            created_at TEXT    NOT NULL,
            ip         TEXT
        )
        """
    )
    conn.execute("CREATE INDEX IF NOT EXISTS idx_score ON scores(score DESC)")
    return conn


def expected_checksum(name: str, score: int, wave: int, kills: int) -> str:
    raw = f"{name}|{score}|{wave}|{kills}|{SECRET}"
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


class ScoreIn(BaseModel):
    name: str
    score: int
    wave: int
    level: int
    kills: int
    checksum: str


class ScoreOut(BaseModel):
    name: str
    score: int
    wave: int
    level: int
    kills: int
    created_at: str


@app.get("/health")
def health():
    return {"ok": True, "time": time.time()}


@app.get("/notice")
def notice():
    """更新公告：改脚本旁边的 notice.json 即可。"""
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "notice.json")
    if not os.path.exists(path):
        raise HTTPException(status_code=404, detail="notice.json not found")
    with open(path, "r", encoding="utf-8-sig") as f:
        return JSONResponse(content=json.load(f))


@app.get("/config")
def config():
    """远程平衡数值：改脚本旁边的 config.json 即可，不用重新打包游戏。"""
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "config.json")
    if not os.path.exists(path):
        raise HTTPException(status_code=404, detail="config.json not found")
    with open(path, "r", encoding="utf-8-sig") as f:
        return JSONResponse(content=json.load(f))


@app.get("/scores")
def top_scores(limit: int = 10):
    limit = max(1, min(int(limit), 50))
    with closing(db()) as conn:
        rows = conn.execute(
            "SELECT name, score, wave, level, kills, created_at "
            "FROM scores ORDER BY score DESC, id ASC LIMIT ?",
            (limit,),
        ).fetchall()
    items = [dict(zip(("name", "score", "wave", "level", "kills", "created_at"), r)) for r in rows]
    return {"items": items}


@app.post("/scores")
def submit(s: ScoreIn, request: Request):
    name = (s.name or "").strip()[:16] or "匿名"

    # 1) 签名校验
    if s.checksum.lower() != expected_checksum(name, s.score, s.wave, s.kills):
        raise HTTPException(status_code=400, detail="checksum mismatch")

    # 2) 数值合理性（挡住随手改分）
    if s.score < 0 or s.score > 10_000_000:
        raise HTTPException(status_code=400, detail="score out of range")
    if s.wave < 1 or s.wave > 9999:
        raise HTTPException(status_code=400, detail="wave out of range")
    if s.kills < 0 or s.kills > 100_000:
        raise HTTPException(status_code=400, detail="kills out of range")
    # 每个击杀最多给 500 分，超过就是伪造
    if s.score > max(100, s.kills * 500):
        raise HTTPException(status_code=400, detail="score/kills mismatch")

    # 3) 频率限制
    ip = request.client.host if request.client else "unknown"
    now = time.time()
    stamps = [t for t in _recent.get(ip, []) if now - t < RATE_WINDOW]
    if len(stamps) >= RATE_LIMIT:
        raise HTTPException(status_code=429, detail="too many submissions")
    stamps.append(now)
    _recent[ip] = stamps

    created = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime())
    with closing(db()) as conn:
        cur = conn.execute(
            "INSERT INTO scores(name, score, wave, level, kills, created_at, ip) "
            "VALUES(?,?,?,?,?,?,?)",
            (name, s.score, s.wave, s.level, s.kills, created, ip),
        )
        conn.commit()
        rank = conn.execute(
            "SELECT COUNT(*) FROM scores WHERE score > ?", (s.score,)
        ).fetchone()[0] + 1

    return {"ok": True, "rank": rank, "id": cur.lastrowid}
