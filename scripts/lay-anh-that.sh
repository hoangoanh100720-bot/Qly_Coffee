#!/usr/bin/env bash
# =============================================================================
#  LẤY ẢNH CHỤP THẬT CHO TOÀN BỘ MENU
# =============================================================================
#  Nguồn: StockSnap.io, tìm qua API Openverse.
#    · Giấy phép CC0 — dùng thương mại tự do, KHÔNG cần ghi công tác giả
#    · Ảnh chụp bằng máy ảnh thật, không phải ảnh dựng bằng AI
#    · Bản gốc 3000–5000px, lấy bản 960px là quá đủ cho thẻ món
#
#  HAI ĐIỀU RÚT RA KHI LÀM (đừng sửa lại cho "gọn hơn"):
#
#  1. TỪ KHÓA PHẢI NGẮN. Openverse khớp theo AND nên càng nhiều từ càng ít
#     kết quả: "iced coffee" ra 11 ảnh, "iced coffee cream" còn 2,
#     "iced coffee cream glass" ra 0. Mỗi món vì thế có một từ khóa chính
#     (sát nghĩa) và một từ khóa dự phòng (rộng hơn).
#
#  2. curl trong Git Bash là BẢN WINDOWS. Nó không hiểu đường dẫn kiểu
#     /tmp/anh.jpg và trả HTTP 000 mà không báo lỗi gì. Phải đổi sang
#     đường dẫn Windows bằng cygpath trước khi đưa vào -F.
#
#  Mỗi ảnh đều kiểm ba lần trước khi dùng: HTTP 200, đúng chữ ký byte JPEG,
#  và đủ lớn (>40KB — file bé thường là ảnh vỡ hoặc ảnh giữ chỗ).
#
#  Không món nào dùng trùng ảnh với món khác.
#  Chạy lại được nhiều lần. Món nào không tìm được thì giữ nguyên hình vẽ SVG.
#
#  CÁCH DÙNG:  bash scripts/lay-anh-that.sh <file-token> <file-danh-sach.tsv>
# =============================================================================
set -uo pipefail

API="http://localhost:5080"
UA="QlyCoffee/1.0"
TMP="$(cygpath "${TEMP:-/tmp}")/qly-anh"
mkdir -p "$TMP"

TOKEN="$(cat "$1")"

# ---- Bản đồ món → "từ khóa chính|từ khóa dự phòng" --------------------------
# Từ khóa tả THỨ TRONG LY chứ không dịch tên món. "Cà phê muối" dịch thành
# "salt coffee" sẽ ra ảnh lọ muối; tả đúng thứ nhìn thấy mới ra ảnh dùng được.
declare -A Q=(
  [ca-phe-muoi]="iced coffee|coffee glass"
  [ca-phe-kem-trung]="coffee foam|cappuccino"
  [bac-xiu]="iced latte|latte"
  [ca-phe-sua-da]="vietnamese coffee|iced coffee"
  [cold-brew]="cold brew|black coffee"
  [americano]="black coffee|espresso"
  [latte]="latte art|coffee cup"
  [tra-sua-tran-chau-duong-den]="bubble tea|milk tea"
  [tra-sua-o-long]="milk tea|iced tea"
  [tra-sua-matcha]="matcha latte|matcha"
  [hong-tra-sua-kem-cheese]="milk tea|tea glass"
  [tra-sua-khoai-mon]="taro|milk tea"
  [tra-dao-cam-sa]="peach tea|iced tea"
  [tra-vai]="lychee|iced tea"
  [tra-chanh-gia-tay]="lemon tea|lemonade"
  [soda-dau-tay]="strawberry soda|soda drink"
  [sinh-to-xoai]="mango smoothie|smoothie"
  [sinh-to-dau]="strawberry smoothie|berry smoothie"
  [cacao-da-xay]="chocolate milkshake|milkshake"
  [matcha-da-xay]="matcha|green tea drink"
  [matcha-latte]="matcha tea|green tea"
  [cacao-nong]="hot chocolate|cocoa"
)

# Nhớ ảnh đã dùng để hai món không đụng cùng một tấm
declare -A USED=()

# Tìm ảnh theo một từ khóa, in ra các đường dẫn tìm được
search() {
  local q="${1// /+}"
  curl -s -m 40 -H "User-Agent: $UA" \
    "https://api.openverse.org/v1/images/?q=$q&license=cc0,pdm&source=stocksnap&page_size=12" \
    | grep -oP '"url":"\Khttps://cdn\.stocksnap\.io[^"]+'
}

printf '%-32s %-8s %-8s %s\n' "MÓN" "TÌM" "CỠ" "KẾT QUẢ"
printf '%s\n' "---------------------------------------------------------------------"

ok=0; fail=0

while IFS=$'\t' read -r id slug name; do
  [ -z "${slug:-}" ] && continue

  pair="${Q[$slug]:-}"
  if [ -z "$pair" ]; then
    printf '%-32s %-8s %-8s %s\n' "$slug" "-" "-" "chưa có từ khóa"
    fail=$((fail+1)); continue
  fi

  primary="${pair%%|*}"
  backup="${pair##*|}"

  # ---- Gom ứng viên: từ khóa chính trước, hết mới tới dự phòng ---------
  mapfile -t urls < <(search "$primary")
  found_by="$primary"
  if [ "${#urls[@]}" -lt 3 ]; then
    mapfile -t more < <(search "$backup")
    urls+=("${more[@]}")
    [ "${#urls[@]}" -gt 0 ] && found_by="$primary + $backup"
  fi

  if [ "${#urls[@]}" -eq 0 ]; then
    printf '%-32s %-8s %-8s %s\n' "$slug" "0" "-" "không tìm thấy, giữ hình vẽ"
    fail=$((fail+1)); continue
  fi

  # ---- Thử từng ảnh tới khi được một tấm hợp lệ và chưa ai dùng --------
  saved=""
  for u in "${urls[@]}"; do
    [ -n "${USED[$u]:-}" ] && continue          # món khác đã dùng tấm này

    f="$TMP/$slug.jpg"
    code="$(curl -s -m 60 -o "$f" -w '%{http_code}' -H "User-Agent: $UA" "$u")"
    [ "$code" != "200" ] && continue

    size=$(stat -c%s "$f" 2>/dev/null || echo 0)
    [ "$size" -lt 40000 ] && continue

    # Chữ ký JPEG: FF D8 FF. Chặn trường hợp máy chủ trả trang lỗi HTML.
    head -c 3 "$f" | od -An -tx1 | tr -d ' ' | grep -q "ffd8ff" || continue

    USED[$u]=1
    saved="$f"
    break
  done

  if [ -z "$saved" ]; then
    printf '%-32s %-8s %-8s %s\n' "$slug" "${#urls[@]}" "-" "tải hỏng, giữ hình vẽ"
    fail=$((fail+1)); continue
  fi

  # ---- Đưa lên qua chính API của hệ thống ------------------------------
  # Đi qua API thay vì chép thẳng vào thư mục, để backend kiểm lại lần nữa và
  # tự cập nhật cột image_url — đúng con đường mà người dùng cũng đi.
  # cygpath: curl bản Windows không hiểu đường dẫn POSIX (xem ghi chú đầu file).
  win="$(cygpath -w "$saved")"
  res="$(curl -s -m 90 -X POST "$API/api/admin/media/product-image?productId=$id" \
    -H "Authorization: Bearer $TOKEN" \
    -F "file=@$win;type=image/jpeg")"

  if printf '%s' "$res" | grep -q '"success":true'; then
    kb=$(( $(stat -c%s "$saved") / 1024 ))
    printf '%-32s %-8s %-8s ✓ %s\n' "$slug" "${#urls[@]}" "${kb}KB" "$found_by"
    ok=$((ok+1))
  else
    msg=$(printf '%s' "$res" | grep -oP '"message":"\K[^"]+' | head -1)
    printf '%-32s %-8s %-8s %s\n' "$slug" "${#urls[@]}" "-" "lỗi: ${msg:-không rõ}"
    fail=$((fail+1))
  fi
done < "$2"

printf '%s\n' "---------------------------------------------------------------------"
printf 'Xong: %d món có ảnh thật, %d món giữ hình vẽ SVG\n' "$ok" "$fail"
