#!/usr/bin/env bash
# ============================================================
# Break Out 排行榜 · 一键部署脚本（Ubuntu / Debian）
#
# 用法（在服务器上、本目录里执行）：
#   bash deploy.sh                 # 自动生成密钥，端口 8000
#   bash deploy.sh my-secret 8080  # 指定密钥和端口
#
# 做完会打印：密钥、访问地址、健康检查结果
# ============================================================
set -euo pipefail

APP_DIR="/opt/breakout-lb"
PORT="${2:-8000}"
SECRET="${1:-$(head -c 32 /dev/urandom | sha256sum | cut -c1-32)}"
SERVICE="breakout-lb"

echo "==> 1/6 检查环境"
command -v python3 >/dev/null || { echo "缺少 python3，请先 apt install python3 python3-venv"; exit 1; }
python3 -c "import venv" 2>/dev/null || { echo "缺少 venv：apt install python3-venv"; exit 1; }

echo "==> 2/6 准备目录 $APP_DIR"
mkdir -p "$APP_DIR"
cp leaderboard_server.py requirements.txt "$APP_DIR"/

echo "==> 3/6 创建虚拟环境并装依赖"
cd "$APP_DIR"
[ -d .venv ] || python3 -m venv .venv
./.venv/bin/pip install --quiet --upgrade pip
./.venv/bin/pip install --quiet -r requirements.txt

echo "==> 4/6 写 systemd 服务"
cat > "/etc/systemd/system/${SERVICE}.service" <<EOF
[Unit]
Description=Break Out Leaderboard
After=network.target

[Service]
WorkingDirectory=${APP_DIR}
Environment="LB_SECRET=${SECRET}"
Environment="LB_DB=${APP_DIR}/leaderboard.db"
ExecStart=${APP_DIR}/.venv/bin/uvicorn leaderboard_server:app --host 0.0.0.0 --port ${PORT}
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now "$SERVICE"
systemctl restart "$SERVICE"

echo "==> 5/6 防火墙"
if command -v ufw >/dev/null 2>&1 && ufw status | grep -q "Status: active"; then
  ufw allow "${PORT}/tcp" >/dev/null || true
  echo "    已放行 ufw ${PORT}/tcp"
else
  echo "    ufw 未启用，跳过（注意云厂商安全组仍要放行 ${PORT}）"
fi

echo "==> 6/6 健康检查"
sleep 2
PUBLIC_IP="$(curl -s --max-time 5 ifconfig.me || echo '你的服务器IP')"
echo
echo "================ 部署完成 ================"
echo "  密钥 SECRET : ${SECRET}"
echo "  端口        : ${PORT}"
echo "  本机检查    : $(curl -s --max-time 5 http://127.0.0.1:${PORT}/health || echo '失败，看 journalctl -u ${SERVICE} -n 50')"
echo "  外网地址    : http://${PUBLIC_IP}:${PORT}"
echo
echo "  下一步：把这行抄进 Unity 的 LeaderboardClient.cs"
echo "    DefaultUrl    = \"http://${PUBLIC_IP}:${PORT}\""
echo "    DefaultSecret = \"${SECRET}\""
echo
echo "  查看日志 : journalctl -u ${SERVICE} -f"
echo "  重启服务 : systemctl restart ${SERVICE}"
echo "========================================="
