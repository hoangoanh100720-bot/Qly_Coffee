# Nguồn ảnh

Toàn bộ ảnh trong thư mục này là **ảnh chụp thật** (không có ảnh dựng bằng AI),
lấy từ hai nguồn, cả hai đều cho dùng thương mại tự do:

- **[Unsplash](https://unsplash.com)** theo [Unsplash License](https://unsplash.com/license) —
  bộ ảnh gốc, ghi ID dạng `photo-…`.
- **CC0 / Public Domain**, tìm qua [Openverse](https://openverse.org) với bộ lọc
  `license=cc0,pdm` — bộ ảnh bổ sung cho các nhóm món mới, ghi đường dẫn đầy đủ.

Phần ghi nguồn dưới đây là để **truy vết khi cần thay ảnh**, không phải nghĩa vụ pháp lý.

> **Ảnh có logo thương hiệu thì loại, không bàn.** Trong lần tìm ảnh cho nhóm
> cookie, kết quả đầu tiên là bánh Oreo còn nguyên chữ dập trên mặt bánh. Đưa
> tấm đó lên thực đơn vừa là quảng cáo không công cho hãng khác, vừa là rủi ro
> nhãn hiệu. Mọi ảnh trong thư mục này đã được xem tận mắt trước khi dùng.

> **Đây là ảnh mẫu.** Khi quán có ảnh chụp món của chính mình, hãy thay thế —
> ảnh thật của quán bán hàng tốt hơn ảnh kho, và Google đánh giá cao nội dung gốc.
> Cách thay: giữ nguyên tên file, ghi đè nội dung. Không cần sửa code
> vì đường dẫn tập trung trong `Services/DrinkPhoto.cs`.

> **Bộ ảnh trong thư mục này giờ là LỚP ĐỠ, không phải ảnh chính.**
> 55 trên 56 món đã có ảnh chụp RIÊNG nằm ở `wwwroot/uploads/products/` (cột
> `image_url` trong database). Ảnh ở đây chỉ hiện khi một món chưa có ảnh riêng —
> hiện tại đúng một món: **Cà phê cốt dừa**, đang mượn `drink-ca-phe-sua-da.webp`
> vì kho CC0 không có ảnh cà phê cốt dừa nào dùng được (kết quả trả về toàn ảnh
> quả dừa tươi hoặc chè dừa, không phải ly cà phê).

## Ảnh món (dùng khi món chưa có `image_url` trong database)

| File | Dùng cho | ID ảnh gốc trên Unsplash |
|---|---|---|
| `drink-ca-phe-sua-da.webp` | Cà phê đá, cà phê sữa | `photo-1517701550927-30cf4ba1dba5` |
| `drink-ca-phe-nong.webp`   | Cà phê nóng           | `photo-1497515114629-f71d768fd07c` |
| `drink-tra-sua.webp`       | Trà sữa, trân châu    | `photo-1558857563-b371033873b8` (cắt `crop=top` để lấy trọn ly) |
| `drink-tra-trai-cay.webp`  | Trà trái cây, soda    | `photo-1499638673689-79a0b5115d87` |
| `drink-da-xay.webp`        | Đá xay, frappe        | `photo-1572490122747-3968b75cc699` |
| `drink-sinh-to.webp`       | Sinh tố               | `photo-1553530666-ba11a7da3888` |
| `drink-nuoc-ep.webp`       | Nước ép               | `photo-1546173159-315724a31696` |
| `drink-mac-dinh.webp`      | Không đoán được nhóm  | `photo-1578314675249-a6910f80cc4e` |

### Ảnh bổ sung cho các nhóm món mới (CC0, qua Openverse)

Thực đơn mở rộng lên hơn 50 món và 8 danh mục, trong đó có cả đồ ăn kèm. Dùng
chung một tấm cho nhiều nhóm thì lưới menu trông như lỗi lặp ảnh, nên mỗi nhóm
dưới đây có ảnh riêng.

| File | Dùng cho | Nguồn gốc |
|---|---|---|
| `drink-espresso.webp`      | Espresso                       | StockSnap `RKVW9F72PL` |
| `drink-matcha.webp`        | Matcha latte, matcha kem cheese | rawpixel `a019-jakubk-0551-matcha-cocktail-in-a-cafe` |
| `drink-socola.webp`        | Socola nóng, cacao nóng, cacao đá | StockSnap `NOXXUWUBGJ` |
| `drink-tra-thao-moc.webp`  | Trà thuần, hoa cúc, gừng, ô long | StockSnap `UASWOBMBJ0` |
| `drink-soda.webp`          | Soda các loại                  | StockSnap `HGO20PXZVV` |
| `drink-sua-chua.webp`      | Sữa chua đánh đá, sữa chua trái cây | StockSnap `QL0I5DPNGX` |
| `food-banh-ngot.webp`      | Tiramisu, bánh phô mai, bánh su kem | rawpixel `px1283402-image-kwvy18j4` |
| `food-croissant.webp`      | Croissant bơ                   | StockSnap `3D35639910` |
| `food-cookie.webp`         | Cookie socola                  | StockSnap `PAUNN1ZOQL` |
| `food-banh-mi.webp`        | Bánh mì que pate               | Wikimedia Commons `Deli_Baguette_Sandwich_(Unsplash).jpg` |

> **Ba nhóm cố tình KHÔNG có ảnh riêng** vì tấm chung đã đúng bản chất món:
> trà trái cây (đào, vải, tắc, ổi, dứa) dùng chung `drink-tra-trai-cay.webp`,
> mọi loại đá xay dùng chung `drink-da-xay.webp`, mọi loại sinh tố dùng chung
> `drink-sinh-to.webp`. Thêm ảnh cho từng vị chỉ làm nặng trang mà khách vẫn
> không phân biệt được ly trà đào với ly trà vải qua một tấm ảnh nhỏ.

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

- **Ảnh món**: `.webp`, cắt vuông `1:1`, cạnh **900px**, chất lượng ~80.
  Tỉ lệ vuông là bắt buộc — nó khớp với `aspect-ratio: 1 / 1` của `.product-visual`
  trong `css/design-system.css` §9.

  > **Vì sao 900px chứ không phải 600px như bản trước.** Khung ảnh ở trang chi
  > tiết món rộng tới ~520px, mà màn hình điện thoại và laptop bây giờ hầu hết là
  > 2x — trình duyệt cần ~1040px điểm ảnh thật để vẽ sắc nét. Ảnh 600px bị kéo
  > giãn gần gấp đôi, nhìn ra ngay là mờ và bệt, nhất là với ly trà có nhiều
  > chi tiết nhỏ (viên đá, lá bạc hà, hạt trân châu). Chất lượng cũng nâng từ 70
  > lên 80 vì WebP ở mức 70 làm nhòe đúng những vùng chuyển màu mềm — bọt sữa,
  > mặt nước — thứ khiến ly nước trông ngon.

- **Ảnh trang trí**: `.webp`, cạnh dài **800–1000px**.
- **`og-cover.jpg`**: phải là **JPG hoặc PNG** đúng **1200×630px**. Không dùng WebP —
  một số bot mạng xã hội chưa đọc được định dạng này.
- Giữ mỗi file dưới ~180KB. Cả thư mục hiện tại khoảng 1,2MB.
- Đặt tên tiếng Việt không dấu, tiền tố `drink-` cho ảnh món, `hero-` cho ảnh hero.

## Lệnh tải lại một ảnh với đúng quy cách

```bash
# Ảnh món (vuông 900px)
curl -L -o wwwroot/img/drink-tra-sua.webp \
  "https://images.unsplash.com/photo-1558857563-b371033873b8?w=900&h=900&q=80&fm=webp&fit=crop&crop=top"

# Ảnh chia sẻ mạng xã hội (1200x630, JPG)
curl -L -o wwwroot/img/og-cover.jpg \
  "https://images.unsplash.com/photo-1461023058943-07fcbe16d735?w=1200&h=630&q=75&fm=jpg&fit=crop"
```

## Ảnh RIÊNG của từng món (`uploads/products/`)

Đây mới là ảnh khách nhìn thấy. Quy trình lấy 36 ảnh cho đợt mở rộng thực đơn —
lặp lại được nếu cần thay:

1. **Tải ứng viên.** Gọi Openverse với bộ lọc `license=cc0,pdm&extension=jpg`,
   3–4 ứng viên mỗi món. Từ khóa tả **thứ trong ly**, không dịch tên món:
   "cà phê muối" dịch thành `salt coffee` sẽ ra ảnh lọ muối.
2. **Ghép bảng ảnh.** Mỗi hàng một món, mỗi cột một ứng viên, **cắt vuông sẵn**
   đúng như lúc dùng thật.
3. **Chọn bằng mắt**, rồi mới cắt 900px WebP và gán qua
   `POST /api/admin/media/product-image?productId=…`.

**Vì sao phải nhìn tận mắt, không lấy kết quả đầu tiên.** Riêng đợt này đã loại:
ảnh dính logo `McCafé`, `Tim Hortons`, `TEASPOON`, `TruMoo`, `Stella Artois`;
một ly trà ổi có tên quán `Banjo Paterson Inn` in trên lót ly — **chỉ lộ ra sau
khi cắt vuông**; ảnh có trẻ em; tranh vẽ thực vật thay vì ảnh chụp; và một loạt
ảnh sai hẳn chủ đề (đàn bò, phi công, đường ray, biển quảng cáo máy kem).

**Giấy phép.** Toàn bộ lọc `cc0,pdm` nên được dùng thương mại tự do và **không
bắt buộc ghi công tác giả** — vì vậy không lưu URL gốc của từng ảnh. Cần đổi ảnh
thì chạy lại quy trình trên, hoặc tải thẳng ảnh của quán ở `/admin/mon`.

**Ảnh của quán luôn thắng.** Chụp món thật rồi tải lên là ghi đè được ngay, không
phải sửa mã nguồn — `DrinkPhoto.PhotoFor()` ưu tiên `image_url` trước tiên.
