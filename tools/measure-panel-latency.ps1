param(
    [string]$EventsIni = (Join-Path $PSScriptRoot "..\..\APIExpose\events.ini"),
    [string]$PanelWebSocket = "ws://127.0.0.1:12345/ws/panel",
    [string]$FrontendWebSocket = "ws://127.0.0.1:12345/ws/frontend",
    [string]$CurrentSystemUrl = "http://127.0.0.1:12345/api/v1/Context/current-system",
    [string[]]$Systems = @("megadrive", "gamegear", "sega32x", "gamegear", "snes", "gamegear"),
    [int]$GapMs = 220,
    [int]$TimeoutMs = 2500,
    [int]$PollMs = 10,
    [switch]$HttpContext,
    [switch]$FrontendOnly
)

$ErrorActionPreference = "Stop"

function Receive-WebSocketText {
    param(
        [System.Net.WebSockets.ClientWebSocket]$WebSocket,
        [int]$TimeoutMs
    )

    $buffer = New-Object byte[] 65536
    $stream = New-Object System.IO.MemoryStream
    $cts = [System.Threading.CancellationTokenSource]::new($TimeoutMs)
    try {
        do {
            $segment = [ArraySegment[byte]]::new($buffer)
            $result = $WebSocket.ReceiveAsync($segment, $cts.Token).GetAwaiter().GetResult()
            if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
                return $null
            }

            $stream.Write($buffer, 0, $result.Count)
        } while (-not $result.EndOfMessage)

        return [System.Text.Encoding]::UTF8.GetString($stream.ToArray())
    }
    catch {
        return $null
    }
    finally {
        $cts.Dispose()
        $stream.Dispose()
    }
}

function Read-PanelSystemId {
    param([string]$Json)

    try {
        $message = $Json | ConvertFrom-Json
        if ($message.Payload.SystemId) {
            return [string]$message.Payload.SystemId
        }
        if ($message.payload.systemId) {
            return [string]$message.payload.systemId
        }
    }
    catch {
    }

    return ""
}

function Write-SystemEvent {
    param([string]$SystemId)

    $content = "event=system-selected`n$SystemId`n"
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $path = Resolve-Path -LiteralPath $EventsIni
    $tmp = "$path.$([Guid]::NewGuid().ToString('N')).tmp"
    [System.IO.File]::WriteAllText($tmp, $content, $utf8NoBom)
    try {
        for ($attempt = 1; $attempt -le 8; $attempt++) {
            try {
                [System.IO.File]::Copy($tmp, $path, $true)
                return
            }
            catch [System.IO.IOException] {
                if ($attempt -eq 8) {
                    throw
                }
                Start-Sleep -Milliseconds (5 * $attempt)
            }
        }
    }
    finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}

$eventsPath = Resolve-Path -LiteralPath $EventsIni

if ($HttpContext) {
    $results = New-Object System.Collections.Generic.List[object]
    foreach ($system in $Systems) {
        $started = [System.Diagnostics.Stopwatch]::StartNew()
        Write-SystemEvent $system
        $seen = ""
        $latency = $null
        $polls = 0

        while ($started.ElapsedMilliseconds -lt $TimeoutMs) {
            $polls++
            try {
                $current = Invoke-RestMethod -Uri $CurrentSystemUrl -TimeoutSec 2
                $seen = [string]$current.system.name
                if ($seen.Equals($system, [StringComparison]::OrdinalIgnoreCase)) {
                    $latency = [int]$started.ElapsedMilliseconds
                    break
                }
            }
            catch {
            }

            Start-Sleep -Milliseconds $PollMs
        }

        $results.Add([pscustomobject]@{
            System = $system
            LatencyMs = $latency
            LastSeen = $seen
            Messages = $polls
        })

        Start-Sleep -Milliseconds $GapMs
    }

    $results | Format-Table -AutoSize
    $valid = @($results | Where-Object { $_.LatencyMs -ne $null })
    if ($valid.Count -gt 0) {
        $avg = [Math]::Round(($valid | Measure-Object -Property LatencyMs -Average).Average, 1)
        $max = ($valid | Measure-Object -Property LatencyMs -Maximum).Maximum
        Write-Host "avg=${avg}ms max=${max}ms success=$($valid.Count)/$($results.Count)"
    }
    return
}

$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$ct = [System.Threading.CancellationToken]::None
$targetWebSocket = if ($FrontendOnly) { $FrontendWebSocket } else { $PanelWebSocket }
[void]$ws.ConnectAsync([Uri]$targetWebSocket, $ct).GetAwaiter().GetResult()

try {
    $results = New-Object System.Collections.Generic.List[object]

    foreach ($system in $Systems) {
        $started = [System.Diagnostics.Stopwatch]::StartNew()
        Write-SystemEvent $system
        $seen = ""
        $messages = 0
        $latency = $null

        while ($started.ElapsedMilliseconds -lt $TimeoutMs) {
            $json = Receive-WebSocketText $ws ([Math]::Max(50, $TimeoutMs - [int]$started.ElapsedMilliseconds))
            if (-not $json) {
                break
            }

            $messages++
            $seen = if ($FrontendOnly) { $json } else { Read-PanelSystemId $json }
            if ($seen.Equals($system, [StringComparison]::OrdinalIgnoreCase)) {
                $latency = [int]$started.ElapsedMilliseconds
                break
            }

            if ($FrontendOnly -and $json -match [Regex]::Escape($system)) {
                $seen = $system
                $latency = [int]$started.ElapsedMilliseconds
                break
            }
        }

        $results.Add([pscustomobject]@{
            System = $system
            LatencyMs = $latency
            LastSeen = $seen
            Messages = $messages
        })

        Start-Sleep -Milliseconds $GapMs
    }

    $results | Format-Table -AutoSize
    $valid = @($results | Where-Object { $_.LatencyMs -ne $null })
    if ($valid.Count -gt 0) {
        $avg = [Math]::Round(($valid | Measure-Object -Property LatencyMs -Average).Average, 1)
        $max = ($valid | Measure-Object -Property LatencyMs -Maximum).Maximum
        Write-Host "avg=${avg}ms max=${max}ms success=$($valid.Count)/$($results.Count)"
    }
}
finally {
    if ($ws.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
        try {
            [void]$ws.CloseOutputAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "done", $ct).GetAwaiter().GetResult()
        }
        catch {
        }
    }
    $ws.Dispose()
}
