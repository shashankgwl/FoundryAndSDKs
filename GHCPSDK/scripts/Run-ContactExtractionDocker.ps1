param(
    [string]$TenantId = $env:AZURE_TENANT_ID,
    [string]$ClientId = $env:AZURE_CLIENT_ID,
    [string]$ClientSecret = $env:AZURE_CLIENT_SECRET,
    [string]$ImageName = "contact-extraction-api",
    [int]$Port = 5111
)

if (-not $TenantId) {
    $TenantId = Read-Host "Azure tenant ID"
}

if (-not $ClientId) {
    $ClientId = Read-Host "Azure client ID"
}

if (-not $ClientSecret) {
    $ClientSecret = Read-Host "Azure client secret"
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    Write-Host "Building container image $ImageName..."
    docker build -f src/ContactExtraction.Api/Dockerfile -t $ImageName .

    Write-Host "Removing any previous container with the same name..."
    docker rm -f $ImageName 2>$null | Out-Null

    Write-Host "Starting container with service-principal credentials..."
    $dockerArgs = @(
        '-d',
        '--name', $ImageName,
        '-p', "$Port:8080",
        '-e', 'ASPNETCORE_URLS=http://+:8080',
        '-e', 'ASPNETCORE_ENVIRONMENT=Production',
        '-e', "AZURE_TENANT_ID=$TenantId",
        '-e', "AZURE_CLIENT_ID=$ClientId",
        '-e', "AZURE_CLIENT_SECRET=$ClientSecret",
        $ImageName
    )

    & docker run @dockerArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Docker container failed to start."
    }

    Write-Host "Container started."
    Write-Host "Run the extraction test with:"
    Write-Host "  .\scripts\Invoke-ContactExtraction.ps1 -FileName 'contact_ppt.pptx'"
}
finally {
    Pop-Location
}
