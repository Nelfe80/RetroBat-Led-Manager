param(
    [string]$Port = "COM3",
    [int]$BaudRate = 115200,
    [string]$FirmwareDir = (Join-Path $PSScriptRoot "..\fw"),
    [string[]]$Files = @("main.py", "hardware_profiles.py", "profiles_db.py"),
    [switch]$NoStopProcesses,
    [switch]$NoSafeBoot,
    [switch]$NoReset
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
    $serial.WriteTimeout = 5000
    $serial.DtrEnable = $true
    $serial.RtsEnable = $true
    $serial.NewLine = "`n"
    $serial.Open()
    Start-Sleep -Milliseconds 500
    return $serial
}

function Read-Until {
    param(
        [System.IO.Ports.SerialPort]$Serial,
        [string]$Needle,
        [int]$TimeoutMs = 3000
    )

    $buffer = ""
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $chunk = $Serial.ReadExisting()
            if ($chunk) {
                $buffer += $chunk
                if ($buffer.Contains($Needle)) {
                    return $buffer
                }
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 20
    }

    return $buffer
}

function Write-Text {
    param(
        [System.IO.Ports.SerialPort]$Serial,
        [string]$Text
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $Serial.BaseStream.Write($bytes, 0, $bytes.Length)
    $Serial.BaseStream.Flush()
}

function Write-Bytes {
    param(
        [System.IO.Ports.SerialPort]$Serial,
        [byte[]]$Bytes
    )

    $Serial.BaseStream.Write($Bytes, 0, $Bytes.Length)
    $Serial.BaseStream.Flush()
}

function Enter-RawRepl {
    param([System.IO.Ports.SerialPort]$Serial)

    $Serial.DiscardInBuffer()
    Write-Bytes $Serial ([byte[]](3, 3))
    Start-Sleep -Milliseconds 250
    [void](Read-Until $Serial ">" 700)

    Write-Bytes $Serial ([byte[]](1))
    $reply = Read-Until $Serial "raw REPL" 2500
    if ($reply -notmatch "raw REPL") {
        throw "Unable to enter MicroPython raw REPL on $Port. Reply was: $reply"
    }
}

function Disable-BootMain {
    param([System.IO.Ports.SerialPort]$Serial)

    Write-Host "[safe-boot] temporarily disabling main.py to avoid watchdog reset during upload" -ForegroundColor DarkCyan
    $code = @"
import os, machine
try:
    os.rename('main.py', 'main.py.deploybak')
except OSError:
    pass
machine.reset()
"@

    try {
        $Serial.DiscardInBuffer()
        Write-Text $Serial $code
        Write-Bytes $Serial ([byte[]](4))
        Start-Sleep -Milliseconds 800
    }
    catch {
    }
}

function Invoke-RawExec {
    param(
        [System.IO.Ports.SerialPort]$Serial,
        [string]$Code,
        [int]$TimeoutMs = 5000
    )

    $Serial.DiscardInBuffer()
    Write-Text $Serial $Code
    Write-Bytes $Serial ([byte[]](4))

    $reply = Read-Until $Serial ([string][char]4) $TimeoutMs
    if ($reply -notmatch "OK") {
        throw "Raw exec was not accepted. Reply was: $reply"
    }

    if ($reply -match "Traceback|Exception|SyntaxError|OSError") {
        throw "Raw exec failed. Reply was: $reply"
    }

    return $reply
}

function Send-FirmwareFile {
    param(
        [System.IO.Ports.SerialPort]$Serial,
        [string]$LocalPath,
        [string]$RemoteName
    )

    if (-not (Test-Path -LiteralPath $LocalPath)) {
        throw "Missing firmware file: $LocalPath"
    }

    $bytes = [System.IO.File]::ReadAllBytes($LocalPath)
    Write-Host "[copy] $RemoteName ($($bytes.Length) bytes)" -ForegroundColor Cyan

    [void](Invoke-RawExec $Serial "f=open('$RemoteName','wb')`nf.close()" 5000)

    $chunkSize = 384
    for ($offset = 0; $offset -lt $bytes.Length; $offset += $chunkSize) {
        $length = [Math]::Min($chunkSize, $bytes.Length - $offset)
        $chunk = New-Object byte[] $length
        [Array]::Copy($bytes, $offset, $chunk, 0, $length)
        $payload = [Convert]::ToBase64String($chunk)
        $code = "import ubinascii`nf=open('$RemoteName','ab')`nf.write(ubinascii.a2b_base64('$payload'))`nf.close()"
        [void](Invoke-RawExec $Serial $code 7000)

        $percent = [int]((($offset + $length) * 100) / [Math]::Max(1, $bytes.Length))
        Write-Progress -Activity "Deploying Pico firmware" -Status $RemoteName -PercentComplete $percent
    }

    Write-Progress -Activity "Deploying Pico firmware" -Completed
    $stat = Invoke-RawExec $Serial "import os`nprint(os.stat('$RemoteName')[6])" 5000
    if ($stat -notmatch [Regex]::Escape($bytes.Length.ToString())) {
        throw "Size verification failed for $RemoteName. Expected $($bytes.Length). Reply was: $stat"
    }
}

$firmwareRoot = Resolve-Path -LiteralPath $FirmwareDir
$serial = $null

try {
    if (-not $NoStopProcesses) {
        Stop-SerialUsers
    }

    Write-Host "[open] $Port @ $BaudRate" -ForegroundColor DarkCyan
    $serial = Open-PicoPort $Port $BaudRate
    Enter-RawRepl $serial

    if (-not $NoSafeBoot -and ($Files -contains "main.py")) {
        Disable-BootMain $serial
        if ($serial -and $serial.IsOpen) {
            $serial.Close()
        }
        if ($serial) {
            $serial.Dispose()
        }

        Start-Sleep -Milliseconds 2600
        Write-Host "[reopen] $Port after safe boot reset" -ForegroundColor DarkCyan
        $serial = Open-PicoPort $Port $BaudRate
        Enter-RawRepl $serial
    }

    foreach ($file in $Files) {
        $local = Join-Path $firmwareRoot $file
        Send-FirmwareFile $serial $local $file
    }

    if ($NoReset) {
        Write-Bytes $serial ([byte[]](2))
        Write-Host "[done] files deployed; left Pico running without reset." -ForegroundColor Green
    }
    else {
        Write-Host "[reset] Pico" -ForegroundColor DarkCyan
        [void](Invoke-RawExec $serial "import machine`nmachine.reset()" 2000)
        Write-Host "[done] files deployed and Pico reset." -ForegroundColor Green
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
