param(
    [string]$Port = "COM3",
    [int]$BaudRate = 115200,
    [switch]$NoStopProcesses
)

$ErrorActionPreference = "Stop"

function Stop-SerialUsers {
    $targets = @()
    $targets += Get-Process LedManager, PicoCommandSender -ErrorAction SilentlyContinue

    $serialBridges = Get-CimInstance Win32_Process -Filter "name='powershell.exe' or name='pwsh.exe'" |
        Where-Object {
            $_.ProcessId -ne $PID -and
            $_.CommandLine -match 'serial-bridge\.ps1'
        }

    foreach ($bridge in $serialBridges) {
        $process = Get-Process -Id $bridge.ProcessId -ErrorAction SilentlyContinue
        if ($process) {
            $targets += $process
        }
    }

    $targets = $targets | Sort-Object Id -Unique
    if ($targets.Count -eq 0) {
        return
    }

    Write-Host "[stop] releasing serial users:" ($targets | ForEach-Object { "$($_.ProcessName):$($_.Id)" }) -ForegroundColor DarkCyan
    $targets | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 900
}

function Open-PicoPort {
    param([string]$PortName, [int]$Speed)

    $serial = New-Object System.IO.Ports.SerialPort $PortName, $Speed, 'None', 8, 'One'
    $serial.ReadTimeout = 250
    $serial.WriteTimeout = 3000
    $serial.DtrEnable = $true
    $serial.RtsEnable = $true
    $serial.NewLine = "`n"
    $serial.Open()
    Start-Sleep -Milliseconds 300
    try { $serial.DiscardInBuffer() } catch {}
    try { $serial.DiscardOutBuffer() } catch {}
    return $serial
}

function Read-Available {
    param([System.IO.Ports.SerialPort]$Serial, [int]$TimeoutMs = 2000)

    $buffer = ""
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $chunk = $Serial.ReadExisting()
            if ($chunk) {
                $buffer += $chunk
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 30
    }

    return $buffer
}

function Write-Bytes {
    param(
        [System.IO.Ports.SerialPort]$Serial,
        [byte[]]$Bytes
    )

    $Serial.BaseStream.Write($Bytes, 0, $Bytes.Length)
    $Serial.BaseStream.Flush()
}

$serial = $null
try {
    if (-not $NoStopProcesses) {
        Stop-SerialUsers
    }

    Write-Host "[open] $Port @ $BaudRate" -ForegroundColor DarkCyan
    $serial = Open-PicoPort $Port $BaudRate

    Write-Host "[reset] Ctrl-C Ctrl-C Ctrl-D" -ForegroundColor DarkCyan
    Write-Bytes $serial ([byte[]](3, 3))
    Start-Sleep -Milliseconds 250
    Write-Bytes $serial ([byte[]](4))
    Start-Sleep -Milliseconds 2600

    $reply = Read-Available $serial 1500
    if ($reply.Trim().Length -gt 0) {
        Write-Host "[reply]" $reply.Trim() -ForegroundColor DarkGray
    }

    try {
        $serial.Write("PING`n")
        Start-Sleep -Milliseconds 250
        $pong = Read-Available $serial 1200
        if ($pong -match "PONG|READY") {
            Write-Host "[ok] Pico reset and responding" -ForegroundColor Green
        }
        else {
            Write-Warning "Pico reset sent, but no PONG/READY observed. Reply: $pong"
        }
    }
    catch {
        Write-Warning "Pico reset sent, but ping failed: $($_.Exception.Message)"
    }
}
finally {
    if ($serial -and $serial.IsOpen) {
        $serial.Close()
    }
    if ($serial) {
        $serial.Dispose()
    }
}
