$ErrorActionPreference = 'Stop'
$ls = Get-Content "C:\Users\shbhide\repos\RoughWork\GHCPSDK\src\ContactExtraction.Api\Properties\launchSettings.json" -Raw | ConvertFrom-Json
$e = $ls.profiles.http.environmentVariables
$ep = $e.FOUNDRY_ENDPOINT.TrimEnd('/')
$deployment = $e.FOUNDRY_DEPLOYMENT_NAME

if ($e.FOUNDRY_API_KEY) {
    Write-Host "Auth: API key (BYOK)"
    $h = @{ Authorization = "Bearer $($e.FOUNDRY_API_KEY)" }
} else {
    Write-Host "Auth: Entra bearer token (FOUNDRY_API_KEY not set)"
    $tok = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$($e.AZURE_TENANT_ID)/oauth2/v2.0/token" -ContentType "application/x-www-form-urlencoded" -Body @{ client_id = $e.AZURE_CLIENT_ID; scope = "https://ai.azure.com/.default"; client_secret = $e.AZURE_CLIENT_SECRET; grant_type = "client_credentials" }
    $h = @{ Authorization = "Bearer $($tok.access_token)" }
}

Write-Host "=== [chat/completions] POST $ep/openai/v1/chat/completions (model=$deployment) ==="
$payload = @{ model = $deployment; messages = @(@{ role = "user"; content = "Reply with one word: pong" }) } | ConvertTo-Json -Depth 5
try {
    $r = Invoke-RestMethod -Method Post -Uri "$ep/openai/v1/chat/completions" -Headers $h -Body $payload -ContentType "application/json"
    Write-Host "  OK (200)"
    Write-Host ("  content: " + $r.choices[0].message.content)
} catch {
    $resp = $_.Exception.Response
    if ($resp) { Write-Host "  HTTP $([int]$resp.StatusCode) $($resp.StatusCode)" }
    if ($_.ErrorDetails.Message) { Write-Host "  $($_.ErrorDetails.Message)" }
}

Write-Host "`n=== [responses] POST $ep/openai/v1/responses (model=$deployment) ==="
$payload2 = @{ model = $deployment; input = "Reply with one word: pong" } | ConvertTo-Json
try {
    $r2 = Invoke-RestMethod -Method Post -Uri "$ep/openai/v1/responses" -Headers $h -Body $payload2 -ContentType "application/json"
    Write-Host "  OK (200) object=$($r2.object)"
} catch {
    $resp = $_.Exception.Response
    if ($resp) { Write-Host "  HTTP $([int]$resp.StatusCode) $($resp.StatusCode)" }
    if ($_.ErrorDetails.Message) { Write-Host "  $($_.ErrorDetails.Message)" }
}
