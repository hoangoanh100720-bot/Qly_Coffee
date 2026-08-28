# Nguồn ảnh

Toàn bộ ảnh trong thư mục này là **ảnh chụp thật**, tải từ [Unsplash](https://unsplash.com)
theo [Unsplash License](https://unsplash.com/license) — được dùng miễn phí cho mục đích
thương mại, không bắt buộc ghi nguồn. Phần ghi nguồn dưới đây là để **truy vết khi cần
thay ảnh**, không phải nghĩa vụ pháp lý.

> **Đây là ảnh mẫu.** Khi quán có ảnh chụp món của chính mình, hãy thay thế —
> ảnh thật của quán bán hàng tốt hơn ảnh kho, và Google đánh giá cao nội dung gốc.
> Cách thay: giữ nguyên tên file, ghi đè nội dung. Không cần sửa code
> vì đường dẫn tập trung trong `Services/DrinkPhoto.cs`.

## Ảnh món (dùng khi món chưa có `image_url` trong database)

| File | Dùng cho | ID ảnh gốc trên Unsplash |
|---|---|---|
| `drink-ca-phe-sua-da.webp` | Cà phê đá, cà phê sữa | `photo-1517701550927-30cf4ba1dba5` |
| `drink-ca-phe-nong.webp`   | Cà phê nóng           | `photo-1497515114629-f71d768fd07c` |
| `drink-tra-sua.webp`       | Trà sữa, trân châu    | `photo-1558857563-b371033873b8` |
| `drink-tra-trai-cay.webp`  | Trà trái cây, soda    | `photo-1621263764928-df1444c5e859` |
| `drink-da-xay.webp`        | Đá xay, frappe        | `photo-1572490122747-3968b75cc699` |
| `drink-sinh-to.webp`       | Sinh tố               | `photo-1553530666-ba11a7da3888` |
| `drink-nuoc-ep.webp`       | Nước ép               | `photo-1546173159-315724a31696` |
| `drink-mac-dinh.webp`      | Không đoán được nhóm  | `photo-1578314675249-a6910f80cc4e` |

## Ảnh trang chủ

| File | Vị trí | ID ảnh gốc |
|---|---|---|
| `hero-ca-phe-da.webp`     | Ảnh DUY NHẤT trong hero       | `photo-1461023058943-07fcbe16d735` |
| `pha-che-thu-cong.webp`   | Thẻ "Định lượng chuẩn"        | `photo-1442512595331-e89e73853f31` |
| `hat-ca-phe-rang.webp`    | Thẻ "Nguyên liệu tươi"        | `photo-1447933601403-0c6688de566e` |
| `ban-be-ca-phe.webp`      | Thẻ "Biết trước còn hay hết"  | `photo-1495474472287-4d71bcdd2085` |
| `khong-gian-quan.webp`    | Khối "Ghé quán"               | `photo-1554118811-1e0d58224f24` |
| `og-cover.jpg`            | Ảnh xem trước khi chia sẻ link | `photo-1461023058943-07fcbe16d735` |

## Quy cách khi thêm hoặc thay ảnh

- **Ảnh món**: `.webp`, cắt vuông `1:1`, cạnh **600px**, chất lượng ~70.
  Tỉ lệ vuông là bắt buộc — nó khớp với `aspect-ratio: 1 / 1` của `.product-visual`
  trong `css/design-system.css` §9.
- **Ảnh trang trí**: `.webp`, cạnh dài **800–1000px**.
- **`og-cover.jpg`**: phải là **JPG hoặc PNG** đúng **1200×630px**. Không dùng WebP —
  một số bot mạng xã hội chưa đọc được định dạng này.
- Giữ mỗi file dưới ~180KB. Cả thư mục hiện tại khoảng 860KB.
- Đặt tên tiếng Việt không dấu, tiền tố `drink-` cho ảnh món, `hero-` cho ảnh hero.

## Lệnh tải lại một ảnh với đúng quy cách

```bash
# Ảnh món (vuông 600px)
curl -L -o wwwroot/img/drink-tra-sua.webp \
  "https://images.unsplash.com/photo-1558857563-b371033873b8?w=600&h=600&q=70&fm=webp&fit=crop"

# Ảnh chia sẻ mạng xã hội (1200x630, JPG)
curl -L -o wwwroot/img/og-cover.jpg \
  "https://images.unsplash.com/photo-1461023058943-07fcbe16d735?w=1200&h=630&q=75&fm=jpg&fit=crop"
```
