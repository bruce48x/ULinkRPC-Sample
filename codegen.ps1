param(
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$serverPath = Join-Path $root 'Server\Server'
$clientPath = Join-Path $root 'Client'

$serverArgs = @(
    'tool', 'run', 'ulinkrpc-codegen', '--',
    '--contracts', (Join-Path $root 'Shared'),
    '--mode', 'server',
    '--server-output', 'Generated',
    '--server-namespace', 'Server.Generated'
)

$clientArgs = @(
    'tool', 'run', 'ulinkrpc-codegen', '--',
    '--contracts', (Join-Path $root 'Shared'),
    '--mode', 'unity',
    '--output', 'Assets\Scripts\Rpc\Generated',
    '--namespace', 'Rpc.Generated'
)

if (-not $NoRestore) {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore failed with exit code $LASTEXITCODE"
    }
}

Push-Location $serverPath
try {
    & dotnet @serverArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Server codegen failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

Push-Location $clientPath
try {
    & dotnet @clientArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Client codegen failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
