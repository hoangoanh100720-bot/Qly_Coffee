#!/usr/bin/env bash
# =============================================================================
#  BƯỚC 1/3 — TẢI ỨNG VIÊN ẢNH
# =============================================================================
#  Chọn ảnh theo kết quả tìm kiếm đầu tiên KHÔNG dùng được. Đã thử và hỏng:
#  gắn thẻ ở kho ảnh rất lỏng nên "bubble tea" trả về ly cà phê nóng, và có
#  tấm còn hiện nguyên logo Starbucks — dùng lên menu là vừa sai luật vừa
#  quảng cáo không công cho đối thủ.
#
#  Nên quy trình tách làm ba bước: tải nhiều ứng viên → ghép thành bảng ảnh
#  để NHÌN → chọn tay rồi mới gán. Bước này là bước một.
# =============================================================================
set -uo pipefail

UA="QlyCoffee/1.0"
OUT="$(cygpath "${TEMP}")/qly-ungvien"
rm -rf "$OUT"; mkdir -p "$OUT"

# Mỗi món vài từ khóa ngắn (Openverse khớp AND, càng dài càng ít kết quả)
declare -A Q=(
  [ca-phe-muoi]="iced coffee;coffee milk;latte"
  [ca-phe-kem-trung]="coffee foam;cappuccino;coffee cream"
  [bac-xiu]="iced latte;milk coffee;latte"
  [ca-phe-sua-da]="iced coffee;coffee ice;cold coffee"
  [cold-brew]="cold brew;iced coffee;coffee glass"
  [americano]="black coffee;espresso;coffee cup"
  [latte]="latte art;cappuccino;coffee cup"
  [tra-sua-tran-chau-duong-den]="bubble tea;boba;milk tea"
  [tra-sua-o-long]="milk tea;bubble tea;tea glass"
  [tra-sua-matcha]="matcha latte;green tea;matcha"
  [hong-tra-sua-kem-cheese]="milk tea;iced tea;tea"
  [tra-sua-khoai-mon]="taro;purple drink;milk tea"
  [tra-dao-cam-sa]="peach tea;iced tea;fruit tea"
  [tra-vai]="lychee;iced tea;fruit tea"
  [tra-chanh-gia-tay]="lemon tea;lemonade;iced tea"
  [soda-dau-tay]="strawberry drink;soda;pink drink"
  [sinh-to-xoai]="mango smoothie;mango juice;smoothie"
  [sinh-to-dau]="strawberry smoothie;berry smoothie;smoothie"
  [cacao-da-xay]="chocolate milkshake;milkshake;frappe"
  [matcha-da-xay]="matcha;green smoothie;green tea"
  [matcha-latte]="matcha tea;green tea;matcha"
  [cacao-nong]="hot chocolate;cocoa;hot drink"
)

while IFS=$'\t' read -r id slug name; do
  [ -z "${slug:-}" ] && continue
  queries="${Q[$slug]:-}"
  [ -z "$queries" ] && continue

  mkdir -p "$OUT/$slug"
  n=0
  declare -A seen=()

  IFS=';' read -ra qs <<< "$queries"
  for q in "${qs[@]}"; do
    [ "$n" -ge 6 ] && break
    esc="${q// /+}"
    mapfile -t urls < <(curl -s -m 40 -H "User-Agent: $UA" \
      "https://api.openverse.org/v1/images/?q=$esc&license=cc0,pdm&source=stocksnap&page_size=12" \
      | grep -oP '"url":"\Khttps://cdn\.stocksnap\.io[^"]+')

    for u in "${urls[@]}"; do
      [ "$n" -ge 6 ] && break
      key="$(basename "$u")"
      [ -n "${seen[$key]:-}" ] && continue
      seen[$key]=1

      f="$OUT/$slug/$n.jpg"
      code="$(curl -s -m 60 -o "$f" -w '%{http_code}' -H "User-Agent: $UA" "$u")"
      [ "$code" != "200" ] && { rm -f "$f"; continue; }
      [ "$(stat -c%s "$f")" -lt 30000 ] && { rm -f "$f"; continue; }
      head -c 3 "$f" | od -An -tx1 | tr -d ' ' | grep -q "ffd8ff" || { rm -f "$f"; continue; }

      echo "$u" > "$OUT/$slug/$n.url"
      n=$((n+1))
    done
  done
  unset seen
  printf '  %-32s %d ứng viên\n' "$slug" "$n"
done < "$1"

echo ""
echo "Thư mục: $OUT"
