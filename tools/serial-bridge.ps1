param(
    [Parameter(Mandatory = $true)]
    [string]$Port,

    [Parameter(Mandatory = $false)]
    [int]$BaudRate = 115200,

    [Parameter(Mandatory = $false)]
    [int]$WriteTimeoutMs = 15000,

    [Parameter(Mandatory = $false)]
    [bool]$ResetOnReopen = $false
)

$ErrorActionPreference = "Stop"

function New-SerialPort {
    $portHandle = New-Object System.IO.Ports.SerialPort $Port, $BaudRate, 'None', 8, 'One'
    $portHandle.ReadTimeout = 120
    $portHandle.WriteTimeout = $WriteTimeoutMs
    $portHandle.DtrEnable = $false
    $portHandle.RtsEnable = $false
    $portHandle.NewLine = "`n"
    return $portHandle
}

function Read-Available {
    param([System.IO.Ports.SerialPort]$PortRef)

    try {
        $reply = $PortRef.ReadExisting()
        if ($reply) {
            foreach ($replyLine in ($reply -split "`r?`n")) {
                if ($replyLine.Trim().Length -gt 0) {
                    Write-Output $replyLine.Trim()
                }
            }
        }
    }
    catch {
    }
}

function Open-SerialPort {
    $maxAttempts = 45
    $retryDelayMs = 1000

    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        $portHandle = $null
        try {
            $portHandle = New-SerialPort
            $portHandle.Open()
            try { $portHandle.DiscardInBuffer() } catch {}
            try { $portHandle.DiscardOutBuffer() } catch {}
            Start-Sleep -Milliseconds 2600
            Read-Available $portHandle | Out-Null
            return $portHandle
        }
        catch {
            if ($portHandle) {
                try { $portHandle.Dispose() } catch {}
            }

            if ($attempt -eq $maxAttempts) {
                throw
            }

            [Console]::Error.WriteLine("[bridge] open attempt $attempt/$maxAttempts failed for ${Port}: $($_.Exception.Message); retrying in ${retryDelayMs}ms")
            Start-Sleep -Milliseconds $retryDelayMs
        }
    }
}

function Send-SerialLine {
    param(
        [System.IO.Ports.SerialPort]$PortRef,
        [string]$Text
    )

    if ($null -eq $PortRef -or -not $PortRef.IsOpen) {
        throw "Serial port $Port is not open"
    }

    if ($null -eq $PortRef.BaseStream) {
        throw "Serial port $Port has no writable base stream"
    }

    $PortRef.Write($Text + "`n")
}

function Reopen-SerialPort {
    param([System.IO.Ports.SerialPort]$PortRef)

    if ($PortRef -and $PortRef.IsOpen) {
        try { $PortRef.Close() } catch {}
    }
    if ($PortRef) {
        try { $PortRef.Dispose() } catch {}
    }

    $reopened = Open-SerialPort
    if ($ResetOnReopen) {
        try {
            [Console]::Out.WriteLine("SERIAL RESET port=$Port reason=reopen")
            $reopened.BaseStream.Write(([byte[]](3, 3)), 0, 2)
            $reopened.BaseStream.Flush()
            Start-Sleep -Milliseconds 250
            $reopened.BaseStream.Write(([byte[]](4)), 0, 1)
            $reopened.BaseStream.Flush()
            Start-Sleep -Milliseconds 2600
            Read-Available $reopened | Out-Null
        }
        catch {
            [Console]::Error.WriteLine("[bridge] reset after reopen failed ${Port}: $($_.Exception.Message)")
        }
    }

    [Console]::Out.WriteLine("SERIAL REOPENED port=$Port baud=$BaudRate")
    return $reopened
}

function Send-With-Reopen {
    param(
        [System.IO.Ports.SerialPort]$PortRef,
        [string]$Text,
        [switch]$RetryOnce
    )

    try {
        Send-SerialLine $PortRef $Text
        return $PortRef
    }
    catch {
        [Console]::Error.WriteLine("[bridge] write failed, reopening ${Port}: $($_.Exception.Message)")
        $PortRef = Reopen-SerialPort $PortRef
        if ($RetryOnce) {
            try {
                Send-SerialLine $PortRef $Text
            }
            catch {
                [Console]::Error.WriteLine("[bridge] retry write failed after reopen ${Port}: $($_.Exception.Message)")
            }
        }

        return $PortRef
    }
}

$serial = $null

try {
    $serial = Open-SerialPort
    $serial = Send-With-Reopen $serial "PING" -RetryOnce
    Start-Sleep -Milliseconds 180
    Read-Available $serial
    Write-Output "READY port=$Port baud=$BaudRate"

    while ($true) {
        $line = [Console]::In.ReadLine()
        if ($null -eq $line) {
            break
        }

        $line = $line.Trim()
        if ($line.Length -eq 0) {
            continue
        }

        # Do not replay a data command immediately after a reopen: the Pico may
        # have rebooted or lost its HW profile, and PicoCommandSender will
        # reinitialize/replay the latest panel after SERIAL REOPENED.
        $serial = Send-With-Reopen $serial $line

        Start-Sleep -Milliseconds 10
        Read-Available $serial
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
