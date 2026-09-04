# Sinh wwwroot/appsettings.json cho Blazor tu cac bien trong .env
# Chay: pwsh scripts/gen-client-config.ps1
#
# Blazor WebAssembly chay trong trinh duyet nen KHONG doc duoc .env. Script nay
# la cau noi: no rut ra dung nhung bien frontend can, ghi thanh mot file JSON
# tinh ma trinh duyet tai ve duoc.
#
# CHI DUA VAO DAY NHUNG GI KHONG PHAI BI MAT. File nay ai mo trinh duyet cung
# doc duoc — dia chi API va so tai khoan nhan tien thi khong sao (so tai khoan
# von da in tren ma QR cho khach quet), nhung khoa API hay mat khau thi tuyet
# doi khong.

$envPath = Join-Path $PSScriptRoot ".." ".env"

# Gia tri mac dinh khi .env thieu bien tuong ung
$apiUrl      = "http://localhost:5080"
$bankCode    = ""
$bankAccount = ""
$bankName    = ""

if (Test-Path $envPath) {
    Get-Content $envPath | ForEach-Object {
        if ($_ -match '^\s*CLIENT_API_URL\s*=\s*(.+)$')       { $apiUrl      = $Matches[1].Trim() }
        if ($_ -match '^\s*BANK_CODE\s*=\s*(.*)$')            { $bankCode    = $Matches[1].Trim() }
        if ($_ -match '^\s*BANK_ACCOUNT_NUMBER\s*=\s*(.*)$')  { $bankAccount = $Matches[1].Trim() }
        if ($_ -match '^\s*BANK_ACCOUNT_NAME\s*=\s*(.*)$')    { $bankName    = $Matches[1].Trim() }
    }
}

$target = Join-Path $PSScriptRoot ".." "frontend/QlyCoffee.Client/wwwroot/appsettings.json"

@{
    ApiBaseUrl = $apiUrl
    Payment    = @{
        BankCode      = $bankCode
        AccountNumber = $bankAccount
        AccountName   = $bankName
    }
} | ConvertTo-Json -Depth 3 | Set-Content -Path $target -Encoding utf8

Write-Host "Da ghi $target voi ApiBaseUrl = $apiUrl"

if ([string]::IsNullOrWhiteSpace($bankCode) -or [string]::IsNullOrWhiteSpace($bankAccount)) {
    Write-Host "Chua khai bao BANK_CODE / BANK_ACCOUNT_NUMBER trong .env - man hinh quay se khong hien ma QR." -ForegroundColor Yellow
} else {
    Write-Host "Ma QR chuyen khoan: $bankCode - $bankAccount"
}
