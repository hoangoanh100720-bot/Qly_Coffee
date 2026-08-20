#!/usr/bin/env bash
# =============================================================================
#  BƯỚC 3/3 — GÁN NHỮNG TẤM ĐÃ CHỌN TAY
# =============================================================================
#  Số thứ tự dưới đây lấy từ bảng ảnh ở bước 2, sau khi đã NHÌN từng tấm.
#  Những tấm bị loại và lý do:
#    · 4 tấm có logo Starbucks   → dùng lên menu là quảng cáo không công
#                                   cho đối thủ, chưa kể rủi ro nhãn hiệu
#    · Ảnh laptop, điện thoại,   → không phải đồ uống
#      bàn ăn sáng, chai bia
#    · Ảnh chụp đúng đồ uống     → cắt vuông là mất hết chủ thể
#      nhưng bố cục quá rộng
#
#  Món nào không có tấm nào đạt thì BỎ TRỐNG, giữ hình vẽ SVG. Hình vẽ đúng
#  màu món còn hơn một tấm ảnh sai món.
# =============================================================================
set -uo pipefail

API="http://localhost:5080"
SRC="$(cygpath "${TEMP}")/qly-ungvien"
TOKEN="$(cat "$1")"

# slug → số thứ tự tấm đã chọn
declare -A PICK=(
  [ca-phe-muoi]=4                 # ly cao, cà phê tách lớp trên nền tối
  [ca-phe-kem-trung]=3            # ly thủy tinh, lớp kem dày phía trên
  [bac-xiu]=1                     # tay cầm ly latte, tông sữa nhiều
  [ca-phe-sua-da]=1               # rót cà phê vào ly sữa — đúng cảnh pha
  [cold-brew]=4                   # ly cao, ống hút, nền gỗ
  [americano]=4                   # cà phê đen chụp từ trên xuống
  [latte]=4                       # latte art hình lá, cận cảnh
  [tra-sua-o-long]=5              # trà đá ly cao có ống hút
  [hong-tra-sua-kem-cheese]=3     # mấy ly cao xếp cạnh nhau
  [tra-sua-khoai-mon]=3           # sinh tố tím — đúng màu khoai môn
  [tra-dao-cam-sa]=2              # trà đá hổ phách trong hũ thủy tinh
  [tra-vai]=3                     # tay cầm ly đồ uống đỏ hồng
  [tra-chanh-gia-tay]=5           # ly cao, trà chanh có bạc hà
  [soda-dau-tay]=5                # đồ uống đỏ trong hũ quai
  [sinh-to-xoai]=5                # ly nước cam vàng trên nền đá
  [sinh-to-dau]=3                 # sinh tố hồng chụp từ trên, có dâu
  [cacao-da-xay]=0                # ba ly sinh tố kem sánh
  [matcha-da-xay]=2               # đồ uống xanh có bạc hà
  [cacao-nong]=0                  # hai ly thủy tinh, kem phủ bột cacao
)

# ---- Kiểm trùng trước khi gán ------------------------------------------------
# Nhiều món dùng chung từ khóa nên hay trả về cùng một tấm. Hai món trên menu
# đội cùng một tấm ảnh là lộ ngay.
declare -A SEEN=()
dup=0
for slug in "${!PICK[@]}"; do
  f="$SRC/$slug/${PICK[$slug]}.jpg"
  [ -f "$f" ] || { echo "  THIẾU FILE: $slug/${PICK[$slug]}.jpg"; dup=1; continue; }
  h="$(md5sum "$f" | cut -d' ' -f1)"
  if [ -n "${SEEN[$h]:-}" ]; then
    echo "  TRÙNG ẢNH: $slug dùng chung tấm với ${SEEN[$h]}"
    dup=1
  fi
  SEEN[$h]="$slug"
done
[ "$dup" -ne 0 ] && { echo "Dừng lại, sửa lựa chọn trước đã."; exit 1; }
echo "Không tấm nào bị trùng. Bắt đầu gán."
echo ""

# ---- Gán --------------------------------------------------------------------
printf '%-32s %s\n' "MÓN" "KẾT QUẢ"
printf '%s\n' "-------------------------------------------------------"
ok=0; skip=0

while IFS=$'\t' read -r id slug name; do
  [ -z "${slug:-}" ] && continue

  idx="${PICK[$slug]:-}"
  if [ -z "$idx" ]; then
    printf '%-32s %s\n' "$slug" "— giữ hình vẽ (chưa có tấm nào đạt)"
    skip=$((skip+1)); continue
  fi

  f="$SRC/$slug/$idx.jpg"
  win="$(cygpath -w "$f")"
  res="$(curl -s -m 90 -X POST "$API/api/admin/media/product-image?productId=$id" \
    -H "Authorization: Bearer $TOKEN" -F "file=@$win;type=image/jpeg")"

  if printf '%s' "$res" | grep -q '"success":true'; then
    kb=$(( $(stat -c%s "$f") / 1024 ))
    printf '%-32s ✓ tấm #%s (%s KB)\n' "$slug" "$idx" "$kb"
    ok=$((ok+1))
  else
    msg=$(printf '%s' "$res" | grep -oP '"message":"\K[^"]+' | head -1)
    printf '%-32s lỗi: %s\n' "$slug" "${msg:-không rõ}"
    skip=$((skip+1))
  fi
done < "$2"

printf '%s\n' "-------------------------------------------------------"
printf 'Xong: %d món có ảnh thật, %d món giữ hình vẽ\n' "$ok" "$skip"
