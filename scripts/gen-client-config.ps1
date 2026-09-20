# =============================================================================
#  SINH wwwroot/appsettings.json CHO CẢ HAI ỨNG DỤNG FRONTEND
#  Chay: pwsh scripts/gen-client-config.ps1
# =============================================================================
#  Blazor WebAssembly chay trong trinh duyet nen KHONG doc duoc .env. Script nay
#  la cau noi: no rut ra dung nhung bien tung app can, ghi thanh file JSON tinh
#  ma trinh duyet tai ve duoc.
#
#  Tu ban tach du an, frontend co HAI app doc lap va moi app can mot bo cau hinh
#  KHAC NHAU:
#
#    • Trang ban hang  — chi can dia chi API. Khong can so tai khoan ngan hang:
#                        khach dat truoc roi toi quan tra tien, man hinh quet ma
#                        QR nam o quay chu khong nam tren dien thoai khach.
#    • App quan ly     — can dia chi API, so tai khoan nhan tien (man hinh ban
#                        tai quay), va dia chi trang ban hang de dat lien ket.
#
#  CHI DUA VAO DAY NHUNG GI KHONG PHAI BI MAT. Hai file nay ai mo trinh duyet
#  cung doc duoc — dia chi API va so tai khoan nhan tien thi khong sao (so tai
#  khoan von da in tren ma QR cho khach quet), nhung khoa API hay JWT_SECRET thi
#  tuyet doi khong.
# =============================================================================

$ErrorActionPreference = "Stop"

$root    = Join-Path $PSScriptRoot ".."
$envPath = Join-Path $root ".env"

# Gia tri mac dinh khi .env thieu bien tuong ung
$apiUrl      = "http://localhost:5080"
$shopUrl     = "http://localhost:5180"
$bankCode    = ""
$bankAccount = ""
$bankName    = ""

if (Test-Path $envPath) {
    Get-Content $envPath | ForEach-Object {
        if ($_ -match '^\s*CLIENT_API_URL\s*=\s*(.+)$')       { $apiUrl      = $Matches[1].Trim() }
        if ($_ -match '^\s*ADMIN_SHOP_URL\s*=\s*(.*)$')       { $shopUrl     = $Matches[1].Trim() }
        if ($_ -match '^\s*BANK_CODE\s*=\s*(.*)$')            { $bankCode    = $Matches[1].Trim() }
        if ($_ -match '^\s*BANK_ACCOUNT_NUMBER\s*=\s*(.*)$')  { $bankAccount = $Matches[1].Trim() }
        if ($_ -match '^\s*BANK_ACCOUNT_NAME\s*=\s*(.*)$')    { $bankName    = $Matches[1].Trim() }
    }
} else {
    Write-Host "Khong tim thay .env - dung toan bo gia tri mac dinh." -ForegroundColor Yellow
}

function Write-Config {
    param([string]$Path, [hashtable]$Data, [string]$Label)

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) {
        throw "Khong thay thu muc $dir - du an da bi di chuyen?"
    }

    $Data | ConvertTo-Json -Depth 3 | Set-Content -Path $Path -Encoding utf8
    Write-Host "  $Label -> $Path"
}

Write-Host "Dang sinh cau hinh frontend tu .env"

# --- Trang ban hang ----------------------------------------------------------
Write-Config -Label "Trang ban hang" `
    -Path (Join-Path $root "frontend/QlyCoffee.Client/wwwroot/appsettings.json") `
    -Data @{ ApiBaseUrl = $apiUrl }

# --- App quan ly -------------------------------------------------------------
Write-Config -Label "App quan ly   " `
    -Path (Join-Path $root "frontend/QlyCoffee.Admin/wwwroot/appsettings.json") `
    -Data @{
        ApiBaseUrl = $apiUrl
        ShopUrl    = $shopUrl
        Payment    = @{
            BankCode      = $bankCode
            AccountNumber = $bankAccount
            AccountName   = $bankName
        }
    }

Write-Host ""
Write-Host "ApiBaseUrl = $apiUrl"
Write-Host "ShopUrl    = $shopUrl"

if ([string]::IsNullOrWhiteSpace($bankCode) -or [string]::IsNullOrWhiteSpace($bankAccount)) {
    Write-Host "Chua khai bao BANK_CODE / BANK_ACCOUNT_NUMBER trong .env - man hinh quay se khong hien ma QR." -ForegroundColor Yellow
} else {
    Write-Host "Ma QR chuyen khoan: $bankCode - $bankAccount"
}
