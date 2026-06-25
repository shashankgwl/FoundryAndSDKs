param(
    [Parameter(Mandatory = $true)]
    [string]$FileName,

    [string]$BaseUrl = "http://localhost:5111"
)

$body = @{ fileName = $FileName } | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Method Post -Uri "$BaseUrl/Extraction" -ContentType "application/json" -Body $body -ErrorAction Stop
    $response | ConvertTo-Json -Depth 10
}
catch {
    Write-Error $_.Exception.Message
    if ($_.ErrorDetails.Message) {
        Write-Error $_.ErrorDetails.Message
    }
    exit 1
}
