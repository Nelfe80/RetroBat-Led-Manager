param(
    [Parameter(Mandatory = $false)]
    [string]$Port = "COM3",

    [Parameter(Mandatory = $false)]
    [int]$BaudRate = 115200,

    [Parameter(Mandatory = $false)]
    [string[]]$Commands = @("PING", "GET")
)

$ErrorActionPreference = "Stop"

if ($Commands.Count -eq 1 -and $Commands[0].Contains(",")) {
    $Commands = $Commands[0].Split(",", [System.StringSplitOptions]::RemoveEmptyEntries).Trim()
}

function Read-SerialAvailable {
    param([System.IO.Ports.SerialPort]$Serial)

    try {
        $reply = $Serial.ReadExisting()
        if ($reply) {
            foreach ($line in ($reply -split "`r?`n")) {
                if ($line.Trim().Length -gt 0) {
                    Write-Output ("RX " + $line.Trim())
                }
            }
        }
    }
    catch {
    }
}

$variants = @(
    @{ Name = "dtr-off-rts-off-lf"; Dtr = $false; Rts = $false; Ending = "`n" },
    @{ Name = "dtr-on-rts-on-lf"; Dtr = $true; Rts = $true; Ending = "`n" },
    @{ Name = "dtr-on-rts-off-lf"; Dtr = $true; Rts = $false; Ending = "`n" },
    @{ Name = "dtr-off-rts-on-lf"; Dtr = $false; Rts = $true; Ending = "`n" },
    @{ Name = "dtr-on-rts-on-crlf"; Dtr = $true; Rts = $true; Ending = "`r`n" }
)

foreach ($variant in $variants) {
    Write-Output ("TRY " + $variant.Name)
    $serial = New-Object System.IO.Ports.SerialPort $Port, $BaudRate, 'None', 8, 'One'
    $serial.Handshake = [System.IO.Ports.Handshake]::None
    $serial.ReadTimeout = 300
    $serial.WriteTimeout = 1800
    $serial.DtrEnable = [bool]$variant.Dtr
    $serial.RtsEnable = [bool]$variant.Rts
    $serial.NewLine = "`n"

    try {
        $serial.Open()
        Start-Sleep -Milliseconds 2500
        Read-SerialAvailable $serial

        foreach ($command in $Commands) {
            Write-Output ("TX " + $command)
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($command + [string]$variant.Ending)
            $serial.BaseStream.Write($bytes, 0, $bytes.Length)
            $serial.BaseStream.Flush()
            Start-Sleep -Milliseconds 250
            Read-SerialAvailable $serial
        }

        Write-Output ("OK " + $variant.Name)
        break
    }
    catch {
        Write-Output ("FAIL " + $variant.Name + " " + $_.Exception.Message)
    }
    finally {
        if ($serial.IsOpen) {
            $serial.Close()
        }
        $serial.Dispose()
        Start-Sleep -Milliseconds 600
    }
}
