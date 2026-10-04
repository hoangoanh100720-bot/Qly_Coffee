# =============================================================================
#  CHẠY CẢ HỆ THỐNG CHO ĐIỆN THOẠI TRUY CẬP ĐƯỢC
# =============================================================================
#  Chạy:   .\scripts\chay-lan.ps1
#
#  Script này tự làm hết những việc mà trước đây phải sửa tay mỗi lần đổi mạng:
#
#    1. Dò địa chỉ IP hiện tại của máy trong mạng LAN
#    2. Ghi địa chỉ đó vào .env (đường dẫn API, CORS…)
#    3. Sinh lại appsettings.json cho hai ứng dụng frontend
#    4. Mở cổng tường lửa (nếu chạy bằng quyền Administrator)
#    5. Khởi động API + trang bán hàng + trang quản lý
#
#  TẠI SAO CẦN SCRIPT NÀY:
#  Router cấp IP động, nên địa chỉ máy đổi mỗi khi đổi Wi-Fi hoặc khởi động lại
#  router. Địa chỉ cũ nằm rải rác ở năm sáu chỗ trong .env và hai file
#  appsettings.json — sửa tay là chắc chắn có ngày sót một chỗ, mà triệu chứng
#  của việc sót thì rất khó đoán: trang mở được nhưng mọi nút bấm đều lỗi mạng.
# =============================================================================

param(
    # Bỏ qua bước build, dùng bản đã biên dịch sẵn cho nhanh
    [switch] $KhongBuild,

    # Chỉ cập nhật cấu hình, không khởi động ứng dụng
    [switch] $ChiCauHinh
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# ---------------------------------------------------------------------------
#  1. DÒ ĐỊA CHỈ IP TRONG MẠNG LAN
# ---------------------------------------------------------------------------
#  Máy thường có nhiều card mạng: Wi-Fi, Ethernet, máy ảo (VirtualBox, WSL,
#  Docker), VPN. Chỉ card nào có ĐƯỜNG RA MẶC ĐỊNH mới là card đang thật sự nối
#  vào mạng có điện thoại. Chọn bừa card đầu tiên là cách chắc chắn lấy nhầm
#  địa chỉ của máy ảo — lúc đó điện thoại không vào được mà không rõ vì sao.

function Get-LanIp {
    $routes = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
              Sort-Object RouteMetric, ifMetric

    foreach ($route in $routes) {
        $ip = Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $route.ifIndex `
                -ErrorAction SilentlyContinue |
              Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
              Select-Object -First 1

        if ($ip) { return $ip }
    }
    return $null
}

$lan = Get-LanIp
if (-not $lan) {
    Write-Host "Khong tim thay dia chi IP nao dang noi mang." -ForegroundColor Red
    Write-Host "Kiem tra lai Wi-Fi hoac day mang roi chay lai."
    exit 1
}

$ip = $lan.IPAddress
Write-Host ""
Write-Host "  Dia chi may trong mang : $ip  ($($lan.InterfaceAlias))" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
#  2. GHI ĐỊA CHỈ VÀO .env
# ---------------------------------------------------------------------------
$envPath = Join-Path $root '.env'
if (-not (Test-Path $envPath)) {
    Write-Host "Khong thay file .env o $envPath" -ForegroundColor Red
    exit 1
}

# CORS giữ CẢ localhost LẪN địa chỉ LAN: máy tính vẫn mở bằng localhost như cũ,
# điện thoại vào bằng IP. Bỏ localhost đi là tự làm hỏng đường dùng hằng ngày.
$cors = @(
    'http://localhost:5180','https://localhost:5181'
    'http://localhost:5190','https://localhost:5191'
    "http://${ip}:5180", "http://${ip}:5190"
) -join ','

$thayThe = @{
    'API_BASE_URL'         = "http://${ip}:5080"
    'CLIENT_BASE_URL'      = "http://${ip}:5180"
    'CLIENT_API_URL'       = "http://${ip}:5080"
    'ADMIN_BASE_URL'       = "http://${ip}:5190"
    'ADMIN_SHOP_URL'       = "http://${ip}:5180"
    'CORS_ALLOWED_ORIGINS' = $cors
}

#  ĐỌC VÀ GHI PHẢI LÀ UTF-8 KHÔNG BOM — hai cái bẫy của PowerShell 5.1:
#
#    · Get-Content không có -Encoding sẽ đọc file UTF-8 theo bảng mã ANSI của
#      Windows. Ghi lại thành UTF-8 là tiếng Việt hỏng hai lần: "Một Chút"
#      biến thành "Má»™t ChÃºt".
#
#    · Set-Content -Encoding utf8 LUÔN chèn BOM vào đầu file. Thư viện DotNetEnv
#      không chịu được BOM, backend sẽ chết ngay lúc khởi động với lỗi
#      "Sprache.ParseException" — nhìn chẳng liên quan gì tới .env cả.
#
#  Dùng thẳng .NET để kiểm soát cả hai.
$utf8KhongBom = New-Object System.Text.UTF8Encoding $false
$noiDung = [System.IO.File]::ReadAllLines($envPath, [System.Text.Encoding]::UTF8)
$daSua = 0

foreach ($key in $thayThe.Keys) {
    $moi = "$key=$($thayThe[$key])"
    $coRoi = $false

    $noiDung = $noiDung | ForEach-Object {
        if ($_ -match "^\s*$key\s*=") { $coRoi = $true; $moi } else { $_ }
    }

    if ($coRoi) { $daSua++ }
    else { Write-Host "  Canh bao: .env khong co dong $key" -ForegroundColor Yellow }
}

[System.IO.File]::WriteAllLines($envPath, $noiDung, $utf8KhongBom)
Write-Host "  Da cap nhat .env       : $daSua bien"

# ---------------------------------------------------------------------------
#  3. SINH LẠI appsettings.json CHO HAI FRONTEND
# ---------------------------------------------------------------------------
#  Blazor WebAssembly chạy TRONG TRÌNH DUYỆT ĐIỆN THOẠI, nên "localhost" với nó
#  nghĩa là chính cái điện thoại đó — không phải máy tính. Địa chỉ API vì thế
#  bắt buộc phải là IP của máy tính, không thể để localhost.
& (Join-Path $PSScriptRoot 'gen-client-config.ps1') | Out-Null
Write-Host "  Da sinh appsettings    : 2 file"

# ---------------------------------------------------------------------------
#  4. MỞ CỔNG TƯỜNG LỬA
# ---------------------------------------------------------------------------
#  Cần quyền Administrator. Không có quyền thì bỏ qua và in lệnh ra để chạy sau —
#  KHÔNG dừng script, vì nhiều máy Windows đã tự hỏi và cho phép từ lần trước.
$quanTri = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$thieuRule = @()
foreach ($cong in 5080, 5180, 5190) {
    $ten = "QlyCoffee $cong"
    if (Get-NetFirewallRule -DisplayName $ten -ErrorAction SilentlyContinue) { continue }

    if ($quanTri) {
        New-NetFirewallRule -DisplayName $ten -Direction Inbound -Protocol TCP `
            -LocalPort $cong -Action Allow -Profile Private, Domain | Out-Null
    } else {
        $thieuRule += $cong
    }
}

if ($thieuRule.Count -gt 0) {
    Write-Host ""
    Write-Host "  Chua mo tuong lua cho cong: $($thieuRule -join ', ')" -ForegroundColor Yellow
    Write-Host "  Neu dien thoai khong vao duoc, mo PowerShell bang quyen Administrator va dan:"
    Write-Host "    foreach (`$p in $($thieuRule -join ',')) { New-NetFirewallRule -DisplayName `"QlyCoffee `$p`" -Direction Inbound -Protocol TCP -LocalPort `$p -Action Allow -Profile Private }" -ForegroundColor Gray
} else {
    Write-Host "  Tuong lua              : da mo 3 cong"
}

if ($ChiCauHinh) {
    Write-Host ""
    Write-Host "  Chi cap nhat cau hinh, khong khoi dong (tham so -ChiCauHinh)." -ForegroundColor Gray
    exit 0
}

# ---------------------------------------------------------------------------
#  5. KHỞI ĐỘNG
# ---------------------------------------------------------------------------
Write-Host ""

# Dừng bản đang chạy trước, nếu không thì build sẽ báo "file dang bi su dung"
Get-Process -Name QlyCoffee.Api, QlyCoffee.Client, QlyCoffee.Admin -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2

if (-not $KhongBuild) {
    Write-Host "  Dang build..." -NoNewline
    $ketQua = & dotnet build (Join-Path $root 'QlyCoffee.sln') --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "BUILD LOI:" -ForegroundColor Red
        $ketQua | Select-String ': error' | Select-Object -First 8
        exit 1
    }
    Write-Host " xong"
}

$ungDung = @(
    @{ Duong = 'backend/QlyCoffee.Api';     Ten = 'API';            Cong = 5080 }
    @{ Duong = 'frontend/QlyCoffee.Client'; Ten = 'Trang ban hang'; Cong = 5180 }
    @{ Duong = 'frontend/QlyCoffee.Admin';  Ten = 'Trang quan ly';  Cong = 5190 }
)

foreach ($app in $ungDung) {
    Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run', '--project', $app.Duong, '--launch-profile', 'http', '--no-build' `
        -WorkingDirectory $root
}

# Chờ cả ba mở cổng. API chậm nhất vì còn áp migration và đồng bộ thực đơn.
#
# Kiểm TỪNG cổng một, không đếm tổng số dòng netstat: mỗi cổng có thể sinh ra
# một hoặc hai dòng tùy máy có IPv6 hay không, nên đếm dòng sẽ báo "đủ ba" trong
# khi thực ra chỉ hai ứng dụng chạy. In ra "xong" lúc backend đã chết là kiểu
# thông báo tệ nhất — người dùng tin là ổn rồi mới phát hiện mọi thứ đều lỗi.
Write-Host "  Dang khoi dong..." -NoNewline
$hetGio = (Get-Date).AddSeconds(120)
$chuaLen = @()

while ((Get-Date) -lt $hetGio) {
    $dangNghe = netstat -an | Select-String 'LISTENING'
    $chuaLen = $ungDung | Where-Object {
        -not ($dangNghe | Select-String ":$($_.Cong) ")
    }
    if ($chuaLen.Count -eq 0) { break }
    Start-Sleep -Milliseconds 700
}

if ($chuaLen.Count -gt 0) {
    Write-Host ""
    Write-Host ""
    Write-Host "  KHONG KHOI DONG DUOC:" -ForegroundColor Red
    foreach ($app in $chuaLen) {
        Write-Host "    $($app.Ten) (cong $($app.Cong))" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "  Chay lenh nay de xem loi cu the:" -ForegroundColor Yellow
    Write-Host "    dotnet run --project $($chuaLen[0].Duong) --launch-profile http" -ForegroundColor Gray
    Write-Host ""
    exit 1
}

Write-Host " xong"

Write-Host ""
Write-Host "  ========================================================" -ForegroundColor Green
Write-Host "   MO TREN DIEN THOAI (phai chung Wi-Fi voi may tinh)" -ForegroundColor Green
Write-Host "  ========================================================" -ForegroundColor Green
Write-Host ""
Write-Host "     Trang quan ly  :  http://${ip}:5190" -ForegroundColor White
Write-Host "     Trang ban hang :  http://${ip}:5180" -ForegroundColor White
Write-Host ""
Write-Host "     Tai khoan      :  admin@qlycoffee.vn"
Write-Host "     Mat khau       :  Admin@123456"
Write-Host ""
Write-Host "  Tren chinh may tinh nay van dung http://localhost:5190 duoc." -ForegroundColor Gray
Write-Host "  Dung ung dung: dong ba cua so dotnet vua mo." -ForegroundColor Gray
Write-Host ""
