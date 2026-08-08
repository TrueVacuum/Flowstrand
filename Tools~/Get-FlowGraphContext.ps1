param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [Parameter(Mandatory = $false)]
    [string]$Graph = "",

    [int]$TimeoutSeconds = 10
)

$resolvedProject = [System.IO.Path]::GetFullPath($ProjectPath)
$bridgeRoot = Join-Path $resolvedProject "Library\Flowstrand\ContextBridge"
$requestDirectory = Join-Path $bridgeRoot "Requests"
$responseDirectory = Join-Path $bridgeRoot "Responses"
[System.IO.Directory]::CreateDirectory($requestDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory($responseDirectory) | Out-Null

$requestId = [System.Guid]::NewGuid().ToString("N")
$requestPath = Join-Path $requestDirectory "$requestId.json"
$responsePath = Join-Path $responseDirectory "$requestId.json"
$requestJson = @{
    id = $requestId
    query = $Graph
} | ConvertTo-Json -Compress

$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($requestPath, $requestJson, $utf8WithoutBom)

$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while (-not [System.IO.File]::Exists($responsePath)) {
    if ([DateTime]::UtcNow -ge $deadline) {
        Remove-Item -LiteralPath $requestPath -ErrorAction SilentlyContinue
        throw "Timed out waiting for Unity. Open the project and wait for script compilation to finish."
    }

    Start-Sleep -Milliseconds 100
}

$responseJson = [System.IO.File]::ReadAllText($responsePath)
Remove-Item -LiteralPath $responsePath -ErrorAction SilentlyContinue
$response = $responseJson | ConvertFrom-Json

if (-not $response.success) {
    $matches = if ($response.matches) {
        " Matches: " + ($response.matches -join ", ")
    } else {
        ""
    }
    throw ($response.error + $matches)
}

if ([string]::IsNullOrWhiteSpace($Graph)) {
    $response.matches
} else {
    $response.context
}
