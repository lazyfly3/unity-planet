param(
    [Parameter(Mandatory = $true)]
    [string]$PipeName,
    [Parameter(Mandatory = $true)]
    [string]$Json,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$client = [System.IO.Pipes.NamedPipeClientStream]::new(
    '.',
    $PipeName,
    [System.IO.Pipes.PipeDirection]::InOut)
$client.Connect(10000)
try {
    $payload = [System.Text.Encoding]::UTF8.GetBytes($Json)
    $length = [BitConverter]::GetBytes([int]$payload.Length)
    $client.Write($length, 0, 4)
    $client.Write($payload, 0, $payload.Length)
    $client.Flush()

    $header = New-Object byte[] 4
    if ($client.Read($header, 0, 4) -ne 4) {
        throw 'Vulcan returned a short response header.'
    }
    $size = [BitConverter]::ToInt32($header, 0)
    $buffer = New-Object byte[] $size
    $offset = 0
    while ($offset -lt $size) {
        $read = $client.Read($buffer, $offset, $size - $offset)
        if ($read -le 0) {
            break
        }
        $offset += $read
    }
    $response = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $offset)
    [System.IO.File]::WriteAllText($OutputPath, $response)
}
catch {
    [System.IO.File]::WriteAllText(
        $OutputPath,
        '{"status":"error","message":"' + $_.Exception.Message.Replace('"', '\"') + '"}')
    throw
}
finally {
    $client.Dispose()
}
