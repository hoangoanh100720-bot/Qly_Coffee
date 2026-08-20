# Sinh wwwroot/appsettings.json cho Blazor từ biến CLIENT_API_URL trong .env
# Chạy: pwsh scripts/gen-client-config.ps1
$envPath = Join-Path $PSScriptRoot ".." ".env"
$apiUrl = "http://localhost:5080"

if (Test-Path $envPath) {
    Get-Content $envPath | ForEach-Object {
        if ($_ -match '^\s*CLIENT_API_URL\s*=\s*(.+)$') { $apiUrl = $Matches[1].Trim() }
    }
}

$target = Join-Path $PSScriptRoot ".." "frontend/QlyCoffee.Client/wwwroot/appsettings.json"
@{ ApiBaseUrl = $apiUrl } | ConvertTo-Json | Set-Content -Path $target -Encoding utf8
Write-Host "Da ghi $target voi ApiBaseUrl = $apiUrl"
