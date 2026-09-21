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
#  KIEM TRA web.config TRUOC KHI GIAO GOI
# -----------------------------------------------------------------------------
#  Mot web.config sai cu phap khong lam publish that bai — no chi la file duoc
#  chep nguyen si. Loi chi lo ra khi IIS doc no, va luc do trieu chung la 500.19
#  cho TOAN BO site, khong phai chi phan cau hinh sai.
#
#  Cai bay da dinh that: dac ta XML cam hai dau gach ngang lien nhau ben trong
#  mot comment. Dung mot dai gach ngang lam duong ke trong comment — thoi quen
#  rat thuong — la file thanh XML khong hop le. Trinh soan thao khong canh bao
#  vi nhin van giong mot comment binh thuong.
#
#  Ba giay kiem tra o day doi lay viec khong phai do loi 500.19 tren may chu.
# -----------------------------------------------------------------------------
function Test-WebConfig {
    # -Static: chi ap dung cho hai goi frontend. Backend khong can MIME hay
    # rewrite — no la ung dung .NET, moi request deu di thang vao ung dung.
    param([string]$Path, [string]$Label, [switch]$Static)

    if (-not (Test-Path $Path)) {
        throw "$Label thieu web.config. Thieu file nay thi IIS tra 404 cho moi duong dan phu (/menu, /ban-hang...) va khong biet kieu file .wasm."
    }

    try {
        $xml = [xml](Get-Content $Path -Raw)
    } catch {
        throw "$Label co web.config SAI CU PHAP XML. IIS se tra 500.19 cho ca site.`n  $Path`n  $($_.Exception.Message)"
    }

    if ($Static) {
        # Ba thu duoi day thieu thi hong theo kieu IM LANG - khong loi, khong
        # canh bao, chi la mot thu ngung hoat dong.
        $mimes = @($xml.SelectNodes("//mimeMap") | ForEach-Object { $_.fileExtension })
        $hau = @{
            ".wasm"        = "app khong khoi dong duoc, trang trang tron"
            ".webp"        = "mat sach anh mon"
            ".webmanifest" = "nut Cai dat bien mat, khong cai duoc app"
        }
        foreach ($need in $hau.Keys) {
            if ($mimes -notcontains $need) {
                Say ("  CANH BAO: {0} chua khai MIME {1} -> {2}" -f $Label, $need, $hau[$need]) Yellow
            }
        }
        if (-not $xml.SelectSingleNode("//rewrite/rules/rule")) {
            Say "  CANH BAO: $Label khong co quy tac rewrite - tai lai trang o duong dan phu se ra 404." Yellow
        }
    }

    Say "  web.config cua $Label : hop le"
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

    # web.config cua backend do SDK tron voi ban goc trong backend/QlyCoffee.Api/.
    # Kiem tra ban KET QUA chu khong phai ban goc, vi buoc tron moi la buoc co
    # the sinh ra file hong.
    Test-WebConfig (Join-Path $apiOut "web.config") "backend API"

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

    Test-WebConfig (Join-Path $TargetDir "web.config") $Label -Static
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
