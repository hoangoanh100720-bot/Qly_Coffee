# =============================================================================
#  SINH BỘ ICON CHO APP QUẢN LÝ (PWA)
#  Chạy: powershell -ExecutionPolicy Bypass -File scripts\gen-admin-icons.ps1
# =============================================================================
#  Android và Windows CHỈ cho phép cài app khi manifest có icon PNG 192 và 512.
#  SVG thì Chrome chấp nhận tùy phiên bản — không đáng đánh cược vào chuyện đó,
#  vì hỏng thì nút "Cài đặt" lặng lẽ biến mất chứ không báo lỗi gì.
#
#  Hình vẽ lấy ĐÚNG path của logo trong mã nguồn, không vẽ lại bằng tay, để icon
#  không bao giờ lệch với logo hiện trên giao diện.
#
#  Dùng WPF (System.Windows.Media) chứ không dùng GDI+: WPF đọc thẳng được cú
#  pháp path của SVG qua Geometry.Parse, còn GDI+ thì phải tự phân tích chuỗi.
# =============================================================================

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$outDir = Join-Path $PSScriptRoot "..\frontend\QlyCoffee.Admin\wwwroot\icons"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

# --- Bảng màu: lấy từ token trong design-system.css --------------------------
$bg     = [System.Windows.Media.Color]::FromRgb(0xCB, 0x3E, 0x0B)  # --accent
$ink    = [System.Windows.Media.Color]::FromRgb(0xFF, 0xFF, 0xFF)
$steam  = [System.Windows.Media.Color]::FromRgb(0xFF, 0xD9, 0xA8)

# --- Hình logo, hệ tọa độ gốc 48x48 ------------------------------------------
$cupPath    = "M12 16h24l-3 22a4 4 0 0 1-4 3.5H19a4 4 0 0 1-4-3.5L12 16Z"
$handlePath = "M36 20h4a5 5 0 0 1 0 10h-3"
$steamPath  = "M20 10c2-3-2-5 0-8M28 10c2-3-2-5 0-8"

function New-Icon {
    param(
        [int]$Size,
        [string]$FileName,
        # Vùng an toàn của icon maskable: hệ điều hành có thể cắt tròn hoặc cắt
        # bo góc tùy máy, nên hình phải co vào giữa để không bị xén mất quai cốc.
        [double]$Inset = 0.0
    )

    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()

    # Nền phủ kín toàn khung. Icon maskable BẮT BUỘC phủ kín — để trong suốt thì
    # Android tự độn nền trắng, nhìn như icon bị lỗi.
    $bgBrush = New-Object System.Windows.Media.SolidColorBrush $bg
    $dc.DrawRectangle($bgBrush, $null,
        (New-Object System.Windows.Rect 0, 0, $Size, $Size))

    # Đưa hệ tọa độ 48x48 của logo về đúng khung, chừa vùng an toàn nếu có.
    $scale = ($Size * (1.0 - 2 * $Inset)) / 48.0
    $offset = $Size * $Inset
    $group = New-Object System.Windows.Media.TransformGroup
    $group.Children.Add((New-Object System.Windows.Media.ScaleTransform $scale, $scale))
    $group.Children.Add((New-Object System.Windows.Media.TranslateTransform $offset, $offset))
    $dc.PushTransform($group)

    $inkBrush   = New-Object System.Windows.Media.SolidColorBrush $ink
    $steamBrush = New-Object System.Windows.Media.SolidColorBrush $steam

    # Nét vẽ dày 2.5 trong hệ 48x48 — cùng giá trị với SVG trên giao diện.
    $penCup = New-Object System.Windows.Media.Pen $inkBrush, 2.5
    $penCup.LineJoin = [System.Windows.Media.PenLineJoin]::Round
    $penSteam = New-Object System.Windows.Media.Pen $steamBrush, 2.5
    $penSteam.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $penSteam.EndLineCap   = [System.Windows.Media.PenLineCap]::Round

    $dc.DrawGeometry($null, $penCup,   [System.Windows.Media.Geometry]::Parse($cupPath))
    $dc.DrawGeometry($null, $penCup,   [System.Windows.Media.Geometry]::Parse($handlePath))
    $dc.DrawGeometry($null, $penSteam, [System.Windows.Media.Geometry]::Parse($steamPath))

    $dc.Pop()
    $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap `
        $Size, $Size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))

    $path = Join-Path $outDir $FileName
    $stream = [System.IO.File]::Create($path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }

    Write-Host ("  {0,-24} {1}x{1}" -f $FileName, $Size)
}

Write-Host "Dang sinh icon vao $outDir"
New-Icon -Size 192 -FileName "icon-192.png"                 -Inset 0.08
New-Icon -Size 512 -FileName "icon-512.png"                 -Inset 0.08
# Vùng an toàn của maskable là 80% chính giữa — nên hình co vào 20% mỗi bên.
New-Icon -Size 512 -FileName "icon-512-maskable.png"        -Inset 0.20
New-Icon -Size 180 -FileName "apple-touch-icon.png"         -Inset 0.08
Write-Host "Xong."
