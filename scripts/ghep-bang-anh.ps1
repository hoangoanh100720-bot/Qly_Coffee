# =============================================================================
#  BƯỚC 2/3 — GHÉP BẢNG ẢNH ĐỂ XEM VÀ CHỌN
# =============================================================================
#  Ghép các ứng viên thành một tấm lưới, mỗi món một hàng, mỗi ô đánh số.
#  Xem một tấm là duyệt được cả nhóm món, thay vì mở từng ảnh một.
#
#  Ô ảnh cắt VUÔNG đúng như thẻ món ngoài trang bán hàng, để thấy luôn phần
#  ảnh sẽ bị cắt mất — nhiều tấm đẹp khi xem toàn khung nhưng cắt vuông là hỏng.
# =============================================================================
param(
    [Parameter(Mandatory)][string[]] $Slugs,
    [Parameter(Mandatory)][string]   $OutFile
)

Add-Type -AssemblyName System.Drawing

$src      = Join-Path $env:TEMP 'qly-ungvien'
$cell     = 200      # cạnh mỗi ô vuông
$labelW   = 190      # bề rộng cột tên món
$pad      = 6
$maxCols  = 6

$rows = @($Slugs | Where-Object { Test-Path (Join-Path $src $_) })
if ($rows.Count -eq 0) { Write-Error "Khong tim thay thu muc anh nao"; exit 1 }

$W = $labelW + ($cell + $pad) * $maxCols + $pad
$H = ($cell + $pad) * $rows.Count + $pad

$bmp = New-Object System.Drawing.Bitmap $W, $H
$g   = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(24,20,18))
$g.InterpolationMode = 'HighQualityBicubic'

$fontName = New-Object System.Drawing.Font 'Segoe UI', 11, ([System.Drawing.FontStyle]::Bold)
$fontIdx  = New-Object System.Drawing.Font 'Segoe UI', 13, ([System.Drawing.FontStyle]::Bold)
$white    = [System.Drawing.Brushes]::White
$yellow   = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,214,102))
$shadow   = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(190,0,0,0))

$y = $pad
foreach ($slug in $rows) {

    # ---- Tên món ở cột trái ------------------------------------------------
    $ty = $y + 4
    $g.DrawString($slug, $fontName, $white, [float]$pad, [float]$ty)

    # ---- Các ô ảnh ---------------------------------------------------------
    $files = @(Get-ChildItem (Join-Path $src $slug) -Filter '*.jpg' | Sort-Object Name)
    $x = $labelW
    foreach ($f in $files) {
        try {
            $img = [System.Drawing.Image]::FromFile($f.FullName)

            # Cắt vuông ở giữa — đúng cách thẻ món hiển thị
            $side = [Math]::Min($img.Width, $img.Height)
            $sx = [int](($img.Width  - $side) / 2)
            $sy = [int](($img.Height - $side) / 2)
            $srcRect = New-Object System.Drawing.Rectangle $sx, $sy, $side, $side
            $dstRect = New-Object System.Drawing.Rectangle $x, $y, $cell, $cell

            $g.DrawImage($img, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
            $img.Dispose()

            # Số thứ tự trên nền tối để luôn đọc được, kể cả trên ảnh sáng
            $n = [IO.Path]::GetFileNameWithoutExtension($f.Name)
            $g.FillRectangle($shadow, $x, $y, 26, 24)
            $nx = $x + 6; $ny = $y + 2
            $g.DrawString($n, $fontIdx, $yellow, [float]$nx, [float]$ny)
        }
        catch {
            $g.DrawString("loi", $fontIdx, $white, [float]$x, [float]$y)
        }
        $x += $cell + $pad
    }
    $y += $cell + $pad
}

$g.Dispose()
$bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"Da ghep: $OutFile  ($W x $H)"
