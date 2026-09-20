# =============================================================================
#  DUNG GOI DEPLOY — san sang keo tha bang WinSCP
#  Chay: powershell -ExecutionPolicy Bypass -File scripts\build-deploy.ps1
# =============================================================================
#  Ket qua nam trong thu muc deploy/ o goc du an, chia lam ba phan tuong ung voi
#  ba thu muc tren may chu:
#
#    deploy/api    -> backend .NET (mot ung dung IIS)
#    deploy/web    -> trang ban hang  (mot site IIS, tinh)
#    deploy/admin  -> app quan ly     (mot site IIS, tinh)
#
#  Ba phan hoan toan doc lap. Sua giao dien trang ban hang thi chi upload lai
#  deploy/web, khong dung toi hai phan kia — backend khong phai khoi dong lai,
#  nhan vien dang ban hang khong bi gian doan.
#
#  THAM SO
#    -ApiUrl    Dia chi backend that, vi du https://api.motchutcoffee.vn
#    -ShopUrl   Dia chi trang ban hang, vi du https://motchutcoffee.vn
#    -SkipApi   Chi dung lai frontend (nhanh hon nhieu khi chi sua giao dien)
#
#  Khong truyen tham so thi script lay gia tri tu .env.
# =============================================================================

[CmdletBinding()]
param(
    [string]$ApiUrl,
    [string]$ShopUrl,
    [switch]$SkipApi
)

$ErrorActionPreference = "Stop"

$root      = Resolve-Path (Join-Path $PSScriptRoot "..")
$deployDir = Join-Path $root "deploy"
$envPath   = Join-Path $root ".env"

function Say([string]$text, [string]$color = "Gray") { Write-Host $text -ForegroundColor $color }

Say ""
Say "=============================================================" Cyan
Say " DUNG GOI DEPLOY - Mot Chut Coffee" Cyan
Say "=============================================================" Cyan

# -----------------------------------------------------------------------------
#  1. Xac dinh dia chi
# -----------------------------------------------------------------------------
if (-not $ApiUrl -or -not $ShopUrl) {
    if (-not (Test-Path $envPath)) {
        throw "Khong thay .env va cung khong truyen -ApiUrl / -ShopUrl."
    }
    Get-Content $envPath | ForEach-Object {
        if (-not $ApiUrl  -and $_ -match '^\s*CLIENT_API_URL\s*=\s*(.+)$')  { $script:ApiUrl  = $Matches[1].Trim() }
        if (-not $ShopUrl -and $_ -match '^\s*ADMIN_SHOP_URL\s*=\s*(.+)$')  { $script:ShopUrl = $Matches[1].Trim() }
    }
}

Say ""
Say "  API  : $ApiUrl"
Say "  Shop : $ShopUrl"

# Canh bao som con hon de phat hien luc da upload xong.
if ($ApiUrl -match 'localhost' -or $ShopUrl -match 'localhost') {
    Say ""
    Say "  CANH BAO: dia chi con la localhost." Yellow
    Say "  Goi nay chi chay duoc tren may ban, KHONG chay duoc tren may chu." Yellow
    Say "  Sua CLIENT_API_URL va ADMIN_SHOP_URL trong .env roi chay lai." Yellow
}

# -----------------------------------------------------------------------------
#  2. Don thu muc cu
# -----------------------------------------------------------------------------
# Xoa han chu khong ghi de: con sot file cua ban truoc thi IIS van phuc vu
# chung, va loi kieu do rat kho lan ra vi moi thu "trong nhu da cap nhat".
if (Test-Path $deployDir) {
    Say ""
    Say "  Don thu muc deploy/ cu..."
    Remove-Item $deployDir -Recurse -Force
}
New-Item -ItemType Directory -Path $deployDir | Out-Null

# -----------------------------------------------------------------------------
#  3. Sinh cau hinh frontend tu .env
# -----------------------------------------------------------------------------
Say ""
Say "  Sinh appsettings.json cho hai app frontend..."
& (Join-Path $PSScriptRoot "gen-client-config.ps1")

function Invoke-Publish {
    param([string]$Project, [string]$OutDir, [string]$Label)

    Say ""
    Say "  Publish $Label..." Cyan

    $projPath = Join-Path $root $Project
    dotnet publish $projPath -c Release -o $OutDir --nologo | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "Publish $Label that bai. Chay lai lenh sau de xem loi day du:`n  dotnet publish $projPath -c Release"
    }
}

# -----------------------------------------------------------------------------
#  4. Backend
# -----------------------------------------------------------------------------
if (-not $SkipApi) {
    $apiOut = Join-Path $deployDir "api"
    Invoke-Publish -Project "backend\QlyCoffee.Api\QlyCoffee.Api.csproj" -OutDir $apiOut -Label "backend API"

    # .env di kem, dat canh file dll. Program.cs tim .env bat dau tu thu muc
    # lam viec roi di nguoc len — duoi IIS thu muc lam viec chinh la thu muc
    # ung dung, nen dat o day la dung cho.
    $apiEnv = Join-Path $apiOut ".env"
    Copy-Item $envPath $apiEnv -Force

    # Thu muc anh tai len. Publish khong tao san vi no rong.
    $uploads = Join-Path $apiOut "wwwroot\uploads\products"
    New-Item -ItemType Directory -Path $uploads -Force | Out-Null

    Say ""
    Say "  LUU Y VE .env TRONG deploy\api\" Yellow
    Say "  File nay vua duoc chep tu .env cua may ban. Truoc khi upload PHAI sua:" Yellow
    Say "    - ASPNETCORE_ENVIRONMENT=Production" Yellow
    Say "    - DB_HOST / DB_PASSWORD  -> theo PostgreSQL tren may chu" Yellow
    Say "    - JWT_SECRET             -> khoa MOI, khong dung lai khoa may dev" Yellow
    Say "    - CORS_ALLOWED_ORIGINS   -> ten mien that cua ca HAI app frontend" Yellow
}

# -----------------------------------------------------------------------------
#  5. Frontend — chi lay NOI DUNG wwwroot
# -----------------------------------------------------------------------------
#  Blazor WebAssembly publish ra hai tang: thu muc goc chua web.config cua SDK,
#  ben trong moi la wwwroot chua trang that. Tren IIS ta tro site thang vao noi
#  dung wwwroot, nen o day chi lay phan ben trong ra.
#
#  web.config that su duoc dung la file nam SAN trong wwwroot cua du an — ban
#  tu viet, co MIME cho .webmanifest va quy tac chong dem sai. Ban cua SDK o
#  tang ngoai bi bo lai, dung y.
function Copy-BlazorOutput {
    param([string]$PublishDir, [string]$TargetDir, [string]$Label)

    $src = Join-Path $PublishDir "wwwroot"
    if (-not (Test-Path $src)) {
        throw "Khong thay $src - publish $Label co ve da that bai."
    }

    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    Copy-Item (Join-Path $src "*") $TargetDir -Recurse -Force

    if (-not (Test-Path (Join-Path $TargetDir "web.config"))) {
        Say "  CANH BAO: $Label thieu web.config - IIS se tra 404 cho moi duong dan phu." Yellow
    }
}

$tmp = Join-Path $env:TEMP "qly-deploy-$(Get-Random)"

try {
    # --- Trang ban hang ---
    $shopTmp = Join-Path $tmp "shop"
    Invoke-Publish -Project "frontend\QlyCoffee.Client\QlyCoffee.Client.csproj" -OutDir $shopTmp -Label "trang ban hang"
    Copy-BlazorOutput -PublishDir $shopTmp -TargetDir (Join-Path $deployDir "web") -Label "trang ban hang"

    # --- App quan ly ---
    $adminTmp = Join-Path $tmp "admin"
    Invoke-Publish -Project "frontend\QlyCoffee.Admin\QlyCoffee.Admin.csproj" -OutDir $adminTmp -Label "app quan ly"
    Copy-BlazorOutput -PublishDir $adminTmp -TargetDir (Join-Path $deployDir "admin") -Label "app quan ly"
}
finally {
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}

# -----------------------------------------------------------------------------
#  6. Bao cao
# -----------------------------------------------------------------------------
function Get-FolderSize([string]$Path) {
    if (-not (Test-Path $Path)) { return "-" }
    $bytes = (Get-ChildItem $Path -Recurse -File | Measure-Object -Property Length -Sum).Sum
    return "{0:N1} MB" -f ($bytes / 1MB)
}

Say ""
Say "=============================================================" Green
Say " XONG" Green
Say "=============================================================" Green
Say ""
Say ("  deploy\api     {0}" -f (Get-FolderSize (Join-Path $deployDir "api")))
Say ("  deploy\web     {0}" -f (Get-FolderSize (Join-Path $deployDir "web")))
Say ("  deploy\admin   {0}" -f (Get-FolderSize (Join-Path $deployDir "admin")))
Say ""
Say "  Buoc tiep theo: docs\08-deploy-iis-winscp.md"
Say ""
