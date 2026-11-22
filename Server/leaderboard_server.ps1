# ============================================================
# Break Out 排行榜 · Windows 版（零依赖，用 PowerShell 内置组件）
#
# 在云电脑（Windows）上：
#   1. 以管理员身份打开 PowerShell
#   2. cd 到本文件所在目录
#   3. $env:LB_SECRET="你的一串密钥"; .\leaderboard_server.ps1
#
# 接口与 Python 版完全一致：
#   GET  /health
#   GET  /scores?limit=10
#   POST /scores  {name,score,wave,level,kills,checksum}
# ============================================================

$ErrorActionPreference = "Stop"

$Port   = if ($env:LB_PORT)   { [int]$env:LB_PORT }   else { 8000 }
$Secret = if ($env:LB_SECRET) { $env:LB_SECRET }      else { "change-me" }
# 数据文件：可用环境变量 LB_DB 覆盖（测试时指向临时文件，避免污染真实数据）
$DbFile = if ($env:LB_DB) { $env:LB_DB } else { Join-Path $PSScriptRoot "scores.json" }
$BackupFile = $DbFile + ".bak"

function Get-Scores {
    if (-not (Test-Path $DbFile)) { return @() }

    try {
        # ReadAllText 比 Get-Content 更保险：能容忍 BOM，也不会按行切分
        $raw = [System.IO.File]::ReadAllText($DbFile, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
        if (-not $raw -or $raw.Trim().Length -eq 0) { return @() }

        $parsed = $raw | ConvertFrom-Json
        $rows = @()

        foreach ($item in @($parsed)) {
            # 只接受"有合法 score 和 name"的条目；坏数据（旧版本写坏的包装格式）直接跳过
            if ($null -eq $item) { continue }
            if ($null -eq $item.name) { continue }

            $scoreValue = $item.score
            if ($scoreValue -is [array]) { continue }
            if (-not ($scoreValue -is [int] -or $scoreValue -is [long] -or $scoreValue -is [double])) { continue }

            $rows += [pscustomobject]@{
                name       = [string]$item.name
                score      = [int]$scoreValue
                wave       = [int]$item.wave
                level      = [int]$item.level
                kills      = [int]$item.kills
                created_at = [string]$item.created_at
            }
        }
        return $rows
    }
    catch {
        Write-Host "[WARN] scores.json 读取失败，已按空榜处理：$($_.Exception.Message)"
        return @()
    }
}

# 数据库最多保留多少条（超出按分数砍掉末尾，避免文件无限膨胀、读取越来越慢）
$Script:MaxRows = 300

function Save-Scores($rows) {
    # 只保留分数最高的 MaxRows 条
    $kept = @($rows | Sort-Object -Property @{Expression={[int]$_.score}; Descending=$true} |
              Select-Object -First $Script:MaxRows)

    # 覆盖前先备份上一版，误删/写坏还能捞回来
    if (Test-Path $DbFile) {
        try { Copy-Item -Path $DbFile -Destination $BackupFile -Force } catch { }
    }

    $json = ConvertTo-Json -InputObject @($kept) -Depth 6
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

    # 原子写入：先写临时文件再替换，避免写到一半崩掉把数据库写坏
    $tmp = "$DbFile.tmp"
    [System.IO.File]::WriteAllText($tmp, $json, $utf8NoBom)
    if (Test-Path $DbFile) { Remove-Item $DbFile -Force -ErrorAction SilentlyContinue }
    Move-Item -Path $tmp -Destination $DbFile -Force
}

function Get-Checksum([string]$name, [int]$score, [int]$wave, [int]$kills) {
    $raw = "{0}|{1}|{2}|{3}|{4}" -f $name, $score, $wave, $kills, $Secret
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($raw))
    return (($bytes | ForEach-Object { $_.ToString("x2") }) -join "")
}

function Send-Json($res, [int]$code, $obj) {
    # 允许直接传已经拼好的 JSON 字符串（数组序列化在 PowerShell 里不可靠，列表用它）
    if ($obj -is [string]) { $json = $obj }
    else { $json = ConvertTo-Json -InputObject $obj -Depth 6 -Compress }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $res.StatusCode = $code
    $res.ContentType = "application/json; charset=utf-8"
    $res.ContentLength64 = $bytes.Length
    $res.OutputStream.Write($bytes, 0, $bytes.Length)
    $res.Close()
}

# "+" 表示监听所有网卡，需要管理员权限；
# 非管理员测试时可以设 $env:LB_PREFIX="http://localhost:8000/" 只用本机回环
$Prefix = if ($env:LB_PREFIX) { $env:LB_PREFIX } else { "http://+:$Port/" }

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add($Prefix)
try {
    $listener.Start()
}
catch {
    Write-Host ""
    Write-Host "[ERROR] 监听失败：$($_.Exception.Message)"
    Write-Host ""
    Write-Host "  最常见原因：已经有一个排行榜服务在跑（端口 8000 被占用）。"
    Write-Host "  解决："
    Write-Host "    1) 关掉其它开着 [Leaderboard listening] 的窗口"
    Write-Host "    2) 或执行下面两行找到并结束它："
    Write-Host "         netstat -ano | findstr :8000"
    Write-Host "         taskkill /F /PID <上面看到的 PID>"
    Write-Host ""
    return
}

Write-Host "=============================================="
Write-Host " Leaderboard listening on port $Port"
Write-Host " Secret : $Secret"
Write-Host " Data   : $DbFile"
Write-Host " 本机测试: http://127.0.0.1:$Port/health"
Write-Host "=============================================="

while ($listener.IsListening) {
    try { $ctx = $listener.GetContext() } catch { break }

    $req  = $ctx.Request
    $res  = $ctx.Response
    $path = $req.Url.AbsolutePath

    try {
        if ($req.HttpMethod -eq "GET" -and $path -eq "/health") {
            Send-Json $res 200 @{ ok = $true; ver = 5; time = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() }
        }
        elseif ($req.HttpMethod -eq "GET" -and $path -eq "/notice") {
            # 更新公告：改脚本旁边的 notice.json 即可
            $noticePath = Join-Path $PSScriptRoot "notice.json"
            if (Test-Path $noticePath) {
                $json = [System.IO.File]::ReadAllText($noticePath, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
                Send-Json $res 200 $json
            } else {
                Send-Json $res 404 @{ detail = "notice.json not found" }
            }
        }
        elseif ($req.HttpMethod -eq "GET" -and $path -eq "/config") {
            # 远程平衡数值：改脚本旁边的 config.json 即可，不用重新打包游戏
            $cfgPath = Join-Path $PSScriptRoot "config.json"
            if (Test-Path $cfgPath) {
                $json = [System.IO.File]::ReadAllText($cfgPath, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
                Send-Json $res 200 $json
            } else {
                Send-Json $res 404 @{ detail = "config.json not found" }
            }
        }
        elseif ($req.HttpMethod -eq "GET" -and $path -eq "/scores") {
            $limit = 10
            $q = $req.QueryString["limit"]
            if ($q) {
                $first = @($q)[0]
                $parsedLimit = 0
                if ([int]::TryParse([string]$first, [ref]$parsedLimit)) {
                    $limit = [Math]::Max(1, [Math]::Min($parsedLimit, 100))
                }
            }

            $rows = @(Get-Scores | Sort-Object -Property @{ Expression = { [int]$_.score } } -Descending |
                      Select-Object -First $limit)

            # 手动拼 JSON：PowerShell 的 ConvertTo-Json 对数组会包一层 {value,Count}
            $parts = foreach ($r in $rows) {
                '{{"name":{0},"score":{1},"wave":{2},"level":{3},"kills":{4},"created_at":{5}}}' -f `
                    (ConvertTo-Json -InputObject ([string]$r.name) -Compress),
                    [int]$r.score, [int]$r.wave, [int]$r.level, [int]$r.kills,
                    (ConvertTo-Json -InputObject ([string]$r.created_at) -Compress)
            }
            $json = '{"items":[' + (($parts | ForEach-Object { $_ }) -join ',') + ']}'

            Send-Json $res 200 $json
        }
        elseif ($req.HttpMethod -eq "POST" -and $path -eq "/scores") {
            $reader = New-Object System.IO.StreamReader($req.InputStream, [System.Text.Encoding]::UTF8)
            $body = $reader.ReadToEnd()
            $reader.Close()

            $data = $body | ConvertFrom-Json

            $name = if ($data.name) { ([string]$data.name).Trim() } else { "" }
            if ($name.Length -eq 0) { $name = "anon" }
            if ($name.Length -gt 16) { $name = $name.Substring(0, 16) }

            $score = [int]$data.score
            $wave  = [int]$data.wave
            $level = [int]$data.level
            $kills = [int]$data.kills
            $sig   = ([string]$data.checksum).ToLower()

            if ($sig -ne (Get-Checksum $name $score $wave $kills)) {
                Send-Json $res 400 @{ detail = "checksum mismatch" }
                continue
            }
            if ($score -lt 0 -or $score -gt 10000000) {
                Send-Json $res 400 @{ detail = "score out of range" }
                continue
            }
            if ($wave -lt 1 -or $wave -gt 9999) {
                Send-Json $res 400 @{ detail = "wave out of range" }
                continue
            }
            if ($kills -lt 0 -or $kills -gt 100000 -or $score -gt [Math]::Max(100, $kills * 500)) {
                Send-Json $res 400 @{ detail = "score/kills mismatch" }
                continue
            }

            $rows = @(Get-Scores)
            $rows += [pscustomobject]@{
                name       = $name
                score      = $score
                wave       = $wave
                level      = $level
                kills      = $kills
                created_at = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
            }
            Save-Scores $rows

            $rank = 1 + (@($rows | Where-Object { [int]$_.score -gt $score }).Count)
            Send-Json $res 200 @{ ok = $true; rank = $rank }
        }
        else {
            Send-Json $res 404 @{ detail = "not found" }
        }
    }
    catch {
        Write-Host "[ERR] $($req.HttpMethod) $path -> $($_.Exception.Message)"
        try { Send-Json $res 500 @{ detail = "$($_.Exception.Message)" } } catch { }
    }
}
