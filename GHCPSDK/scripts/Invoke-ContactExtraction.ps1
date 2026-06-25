param(
    [Parameter(Mandatory = $false)]
    [string[]]$FileName = @("cartoon_specimen_id_06.pdf", "specimen_id_05_preview.png", "contact_ppt.pptx"),

    [string]$BaseUrl = "http://localhost:5111"
)

foreach ($name in $FileName) {
    $body = @{ fileName = $name } | ConvertTo-Json

    try {
        Write-Host "Calling extraction for: $name"
        $response = Invoke-RestMethod -Method Post -Uri "$BaseUrl/Extraction" -ContentType "application/json" -Body $body -ErrorAction Stop
        $response | ConvertTo-Json -Depth 10 | ForEach-Object {
            Write-Host $_ -ForegroundColor DarkGreen
        }
    }
    catch {
        Write-Error "Failed for $name"
        Write-Error $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            Write-Error $_.ErrorDetails.Message
        }
        exit 1
    }
}
