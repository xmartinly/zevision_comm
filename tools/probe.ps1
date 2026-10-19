<#
.SYNOPSIS
    Sends raw Zevision frames to an IMC300 and prints the raw replies (for bring-up tests, docs/联调记录.md).

.DESCRIPTION
    Each step is "<format>|<command>", where format is P (plain), L (length bytes), C (checksum) or LC (both),
    or "WAIT|<ms>". The frame is built exactly as given, so the device's reaction to a wrong format can be seen.
    Every reply is printed as text (non-printable bytes as <XX>) and hex, with the time to the first and last byte.
    Close Zevision.exe first: the port can be open in one program only.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\probe.ps1 -Port COM3 -Steps 'P|H01','LC|H01','P|QG1304;QG1305'

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\probe.ps1 -TcpHost 192.168.5.88 -Steps 'LC|H01','LC|SC11;SC12'
#>
param(
    [string]$Port,
    [string]$TcpHost,
    [int]$Baud = 115200,
    [int]$IdleMs = 600,
    [int]$FirstByteMs = 3000,
    [Parameter(Mandatory = $true)][string[]]$Steps
)

$ErrorActionPreference = 'Stop'
if (-not $Port -and -not $TcpHost) { throw 'Give -Port (RS232) or -TcpHost (TCP port 2101).' }

function New-Frame([string]$format, [string]$command) {
    $data = [System.Text.Encoding]::ASCII.GetBytes($command)
    $frame = New-Object System.Collections.Generic.List[byte]
    $frame.Add(0x7B)
    if ($format -match 'L') { $frame.Add([byte](($data.Length -shr 8) -band 0xFF)); $frame.Add([byte]($data.Length -band 0xFF)) }
    $frame.AddRange($data)
    if ($format -match 'C') { $sum = 0; foreach ($b in $data) { $sum = ($sum + $b) % 256 }; $frame.Add([byte]$sum) }
    $frame.Add(0x7D)
    return , $frame.ToArray()
}

function Format-Bytes([byte[]]$bytes) {
    $text = New-Object System.Text.StringBuilder
    foreach ($b in $bytes) {
        if ($b -ge 0x20 -and $b -lt 0x7F) { [void]$text.Append([char]$b) } else { [void]$text.Append('<' + $b.ToString('X2') + '>') }
    }
    return $text.ToString()
}

function Show([string]$direction, [byte[]]$bytes, [int]$max = 400) {
    $shown = if ($bytes.Length -gt $max) { $bytes[0..($max - 1)] } else { $bytes }
    $suffix = if ($bytes.Length -gt $max) { " ... ($($bytes.Length) bytes)" } else { " ($($bytes.Length) bytes)" }
    "{0} TEXT {1}{2}" -f $direction, (Format-Bytes $shown), $suffix
    "{0} HEX  {1}" -f $direction, (($shown | ForEach-Object { $_.ToString('X2') }) -join ' ')
    if ($bytes.Length -gt $max) { "{0} TAIL {1}" -f $direction, (Format-Bytes $bytes[($bytes.Length - 40)..($bytes.Length - 1)]) }
}

$serial = $null; $client = $null
if ($Port) {
    $serial = New-Object System.IO.Ports.SerialPort $Port, $Baud, ([System.IO.Ports.Parity]::None), 8, ([System.IO.Ports.StopBits]::One)
    $serial.Open()
    $stream = $serial.BaseStream
    "Opened $Port, $Baud, 8N1"
} else {
    $client = New-Object System.Net.Sockets.TcpClient
    if (-not $client.ConnectAsync($TcpHost, 2101).Wait(5000)) { throw "No TCP connection to $TcpHost`:2101 within 5 s." }
    $client.NoDelay = $true
    $stream = $client.GetStream()
    "Connected to $TcpHost`:2101"
}

function Get-Available { if ($serial) { $serial.BytesToRead } else { $client.Available } }

try {
    Start-Sleep -Milliseconds 300
    $stale = Get-Available
    if ($stale -gt 0) { $buffer = New-Object byte[] $stale; [void]$stream.Read($buffer, 0, $stale); Show 'STALE' $buffer }

    foreach ($step in $Steps) {
        $format, $command = $step.Split('|', 2)
        if ($format -eq 'WAIT') { Start-Sleep -Milliseconds ([int]$command); continue }

        $frame = New-Frame $format $command
        ''
        "=== [$format] $command"
        Show 'TX' $frame
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        $stream.Write($frame, 0, $frame.Length)
        $stream.Flush()

        $received = New-Object System.Collections.Generic.List[byte]
        $first = -1; $last = 0
        while ($true) {
            $count = Get-Available
            if ($count -gt 0) {
                $buffer = New-Object byte[] $count
                $read = $stream.Read($buffer, 0, $count)
                $received.AddRange([byte[]]$buffer[0..($read - 1)])
                if ($first -lt 0) { $first = $clock.ElapsedMilliseconds }
                $last = $clock.ElapsedMilliseconds
            } elseif (($first -lt 0 -and $clock.ElapsedMilliseconds -gt $FirstByteMs) -or ($first -ge 0 -and $clock.ElapsedMilliseconds - $last -gt $IdleMs)) {
                break
            } else {
                Start-Sleep -Milliseconds 10
            }
        }

        if ($received.Count -eq 0) { "RX   no reply within $FirstByteMs ms" }
        else { "RX   first byte {0} ms, last {1} ms" -f $first, $last; Show 'RX' $received.ToArray() }
    }
} finally {
    if ($serial) { $serial.Close(); $serial.Dispose() }
    if ($client) { $client.Close() }
}
