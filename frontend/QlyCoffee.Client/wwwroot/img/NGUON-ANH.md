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

> **Bộ ảnh trong thư mục này là LỚP ĐỠ, không phải ảnh chính.** Ảnh chính là ảnh
> quán tự chụp, nằm ở `wwwroot/uploads/products/` (cột `image_url`).
>
> Đếm trên database ngày 22/09/2026: **29 trên 56 món đã có ảnh riêng, 27 món
> còn lại đang dùng ảnh mẫu.** Con số này đổi mỗi lần quán tải ảnh lên, kiểm lại
> bằng:
>
> ```sql
> select count(*) filter (where image_url is null or image_url = '') as chua_co,
>        count(*) as tong
> from products where deleted_at is null and is_active;
> ```

## Ảnh món (dùng khi món chưa có `image_url` trong database)

| File | Dùng cho | ID ảnh gốc trên Unsplash |
|---|---|---|
| `drink-ca-phe-sua-da.webp` | Cà phê đá, cà phê sữa | `photo-1517701550927-30cf4ba1dba5` |
| `drink-ca-phe-nong.webp`   | Cà phê nóng           | `photo-1497515114629-f71d768fd07c` |
| `drink-tra-sua.webp`       | Trà sữa, trân châu    | `photo-1558857563-b371033873b8` (cắt `crop=top` để lấy trọn ly) |
| `drink-tra-trai-cay.webp`  | Trà trái cây, soda    | `photo-1499638673689-79a0b5115d87` |
| `drink-da-xay.webp`        | Đá xay, frappe        | `photo-1572490122747-3968b75cc699` |
| `drink-sinh-to.webp`       | Sinh tố quả mọng (dâu, việt quất) | `photo-1553530666-ba11a7da3888` |
| `drink-sinh-to-xoai.webp`  | Sinh tố xoài          | `photo-1623065422902-30a2d299bbe4` |
| `drink-sinh-to-bo.webp`    | Sinh tố bơ            | Pixabay `drink-4972711` (bams_awey), cắt vuông từ bản 853×1280 |
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

> **Dùng chung ảnh nhóm được tới đâu.** Trà trái cây (đào, vải, tắc, ổi, dứa)
> dùng chung `drink-tra-trai-cay.webp` và mọi loại đá xay dùng chung
> `drink-da-xay.webp` — những ly này cùng màu, cùng kiểu ly, khách nhìn ảnh nhỏ
> không phân biệt được nên một tấm là đủ.
>
> **Sinh tố thì KHÔNG.** Ảnh nhóm là sinh tố quả mọng, tức màu đỏ tím. Ly sinh
> tố xoài màu vàng và ly sinh tố bơ màu xanh nhạt — dùng chung ảnh đỏ là nói
> sai về thứ khách sắp nhận. Vì vậy xoài và bơ mỗi món một ảnh riêng.
>
> **Tìm ảnh sinh tố bơ mất công hơn hẳn các món khác.** Unsplash, Wikimedia
> Commons, StockSnap và Pexels đều không có: từ khóa "avocado smoothie" ở những
> kho này trả về sinh tố rau chân vịt, kiwi, dưa leo — màu xanh đậm, không phải
> ly bơ xay sữa. Hai kết quả gần đúng nhất lại là ảnh chụp điện thoại **có logo
> thương hiệu trà sữa khác** in trên ly, loại thẳng.
>
> Tấm đang dùng lấy từ **Pixabay** (`drink-4972711`, tác giả bams_awey) — giấy
> phép Pixabay Content License, dùng thương mại tự do, không cần ghi nguồn. Bản
> gốc là ảnh dọc 853×1280 nên phải cắt vuông lấy phần thân ly; lệnh cắt nằm ở
> mục "Quy cách" bên dưới.

## Ảnh trang chủ

| File | Vị trí | ID ảnh gốc |
|---|---|---|
| `hero-ca-phe-da.webp`     | Ảnh DUY NHẤT trong hero       | `photo-1461023058943-07fcbe16d735` |
| `pha-che-thu-cong.webp`   | Thẻ "Định lượng chuẩn"        | `photo-1442512595331-e89e73853f31` |
| `hat-ca-phe-rang.webp`    | Thẻ "Nguyên liệu tươi"        | `photo-1447933601403-0c6688de566e` |
| `ban-be-ca-phe.webp`      | Thẻ "Biết trước còn hay hết"  | `photo-1495474472287-4d71bcdd2085` |
| `khong-gian-quan.webp`    | Thẻ + hero trang không gian   | `photo-1757010055832-de355d2f8f06` |
| `og-cover.jpg`            | Ảnh xem trước khi chia sẻ link | `photo-1461023058943-07fcbe16d735` |

## Ảnh trang câu chuyện (`/cau-chuyen/...`)

| File | Vị trí | ID ảnh gốc | Tác giả |
|---|---|---|---|
| `story-latte-art.webp`        | Hero trang workshop            | — | — |
| `khong-gian-quan.webp`        | Hero trang không gian, 1600×900 | `photo-1757010055832-de355d2f8f06` | Caroline Badran |
| `khong-gian-ban-dai.webp`     | Dải ảnh — "Khu chung", 1200×800 | `photo-1747928272448-49524fcb5cfb` | Raymond Yeung |
| `khong-gian-goc-yen.webp`     | Dải ảnh — "Khu riêng", 800×1066 | `photo-1749871615234-98bff62995ba` | Kouji Tsuru |
| `khong-gian-ngoai-troi.webp`  | Dải ảnh — "Ngoài trời", 800×1066 | `photo-1759050483129-512154ddd640` | Johan Mouchet |

Cả bốn tấm đều Unsplash License (dùng thương mại, không cần ghi nguồn), chụp
bằng máy ảnh thật — không phải ảnh dựng bằng AI, không có logo thương hiệu.

**`khong-gian-quan.webp` ĐÃ ĐƯỢC THAY** (bản cũ: `photo-1554118811-1e0d58224f24`).
Bản cũ có bảng hiệu **"BROEI"** và bảng menu tiếng Hà Lan chiếm gần nửa khung —
tức là đăng ảnh quán khác, kèm nguyên tên thương hiệu của họ, lên trang giới
thiệu không gian quán mình. Tông ảnh cũng xám lạnh, chỏi với bảng màu ấm của
cả site.

**Đã loại trong lúc chọn**, ghi lại để khỏi tìm lại lần sau:
- `photo-1743419672503-3e363bcd3634` — quán Hàn, tông xám lạnh, dày poster
  thương hiệu và chữ Hàn.
- `photo-1543269865-cbf427effbad` — nhóm bạn ở quán, ấm và hợp, nhưng lộ nắp
  laptop có logo và trùng ý với `ban-be-ca-phe.webp` đang dùng.
- `photo-1754982905667-f3f62cfb002d` — góc nắng nhiều cây, ảnh đẹp nhưng tối
  và ngả lạnh, đặt cạnh ba tấm kia thì lệch tông.

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

### Khi nguồn ảnh KHÔNG cắt sẵn được

Unsplash cắt vuông ngay trên đường dẫn (`&fit=crop`), nhưng Pixabay và Wikimedia
thì không — phải tải bản gốc rồi tự cắt. Công cụ cắt dùng SkiaSharp, mỗi lần cần
thì dựng lại bằng vài dòng:

```csharp
// dotnet new console && dotnet add package SkiaSharp && dotnet add package SkiaSharp.NativeAssets.Win32
using var goc = SKBitmap.Decode("anh-goc.jpg");
var canh = Math.Min(goc.Width, goc.Height);
var top  = (int)Math.Clamp(goc.Height * 0.45 - canh / 2.0, 0, goc.Height - canh);  // 0.45 = ngắm vào thân ly
using var cat = new SKBitmap(canh, canh);
goc.ExtractSubset(cat, SKRectI.Create((goc.Width - canh) / 2, top, canh, canh));
using var resize = cat.Resize(new SKImageInfo(900, 900), SKFilterQuality.High);
SKImage.FromBitmap(resize).Encode(SKEncodedImageFormat.Webp, 80).SaveTo(File.OpenWrite("ra.webp"));
```

Tỉ lệ ngắm dọc quan trọng hơn vẻ ngoài: ảnh đồ uống chụp dọc thường có ly ở nửa
trên, cắt giữa theo mặc định sẽ mất lớp kem trên mặt — đúng phần làm ly nước
trông ngon nhất.

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

## Ảnh topping

Nằm ở `QlyCoffee.Ui/wwwroot/img/` (dùng chung cho trang bán hàng và màn bán tại
quầy), tra theo tên topping trong `QlyCoffee.Ui/Services/ToppingPhoto.cs`.
Cắt vuông giữa ảnh, 480×480, WebP.

Bộ này dùng thêm giấy phép **CC BY** và **CC BY-SA** — cả hai đều cho dùng
thương mại, **điều kiện là ghi tác giả và giấy phép**, nên bảng dưới đây là
NGHĨA VỤ chứ không chỉ để truy vết. Thay ảnh khác thì sửa luôn dòng tương ứng.
Lý do mở rộng: kho CC0 gần như không có ảnh chụp thật của topping châu Á
(sương sáo, củ năng, hạt nổ…) — kết quả toàn tranh minh hoạ hoặc ảnh sai món.

| File | Topping | Giấy phép | Tác giả | Nguồn |
|---|---|---|---|---|
| `topping-tran-chau-den.webp` | Trân châu đen | CC0 | sam651030 from Pixabay | https://commons.wikimedia.org/w/index.php?curid=98925900 |
| `topping-tran-chau-trang.webp` | Trân châu trắng | CC BY-SA | Jul Lllll | https://commons.wikimedia.org/w/index.php?curid=76655245 |
| `topping-thach-nha-dam.webp` | Thạch nha đam | CC BY-SA | Gunawan Kartapranata | https://commons.wikimedia.org/w/index.php?curid=33212891 |
| `topping-pudding-trung.webp` | Pudding trứng | CC BY | Bex.Walton | https://www.flickr.com/photos/7831824@N04/52017185277 |
| `topping-kem-cheese.webp` | Kem cheese | CC BY-SA | Vincent60030 | https://commons.wikimedia.org/wiki/File:Regiustea_Cheese_Brown_Sugar.jpg |
| `topping-banh-quy-nghien.webp` | Bánh quy nghiền | CC BY-SA | Pittigrilli | https://commons.wikimedia.org/w/index.php?curid=119598584 |
| `topping-hat-no.webp` | Hạt nổ | CC BY-SA | Coldsea.eu | https://commons.wikimedia.org/w/index.php?curid=99234869 |
| `topping-cu-nang.webp` | Củ năng (hạt lựu củ năng đỏ) | CC BY-SA | Chensiyuan at English Wikipedia | https://commons.wikimedia.org/w/index.php?curid=25366115 |
| `topping-suong-sao.webp` | Sương sáo | CC0 | EYAMXAOLP | https://commons.wikimedia.org/w/index.php?curid=149485982 |
| `topping-thach-ca-phe.webp` | Thạch cà phê | CC BY-SA | Lombroso | https://commons.wikimedia.org/w/index.php?curid=39747606 |
| `topping-thach-dua.webp` | Thạch dừa | CC0 | Judgefloro | https://commons.wikimedia.org/w/index.php?curid=61428904 |
| `topping-thach-trai-cay.webp` | Thạch trái cây | CC0 | Obsidian Soul | https://commons.wikimedia.org/w/index.php?curid=75228609 |

> Ảnh "Kem cheese" CẮT từ ảnh một ly trà sữa kem cheese của thương hiệu khác: chỉ lấy
> phần lớp kem sệt phía trên, logo in giữa thân ly nằm NGOÀI khung cắt (cắt tại
> x=170, y=25, cạnh 640px trên bản 960px). Khi đổi ảnh, giữ nguyên quy tắc này.
> Quán chụp được ảnh thật của mình thì thay file này trước.
