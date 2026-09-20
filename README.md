# Qly Coffee

> Web bán hàng của quán cà phê với menu hiện đại cho giới trẻ, có **công thức định lượng**
> chuẩn cho từng món, **tự động trừ nguyên liệu tồn kho** theo từng đơn, và mỗi tối tự sinh
> **bản kế hoạch xả hàng cận hạn** gửi về trang quản lý.

**Trạng thái:** Đã chạy thật và kiểm chứng end-to-end trên .NET 8 + PostgreSQL 18
(đặt đơn → FEFO trừ đúng nguyên liệu → sinh kế hoạch cận hạn).

---

## Chạy trong 5 phút

**Bước 1 — PostgreSQL.** Có sẵn bản cài trên máy thì dùng luôn, chỉ cần sửa
`DB_PASSWORD` trong `.env` cho khớp. Không có thì dựng bằng Docker:

```bash
docker compose up -d
```

**Bước 2 — Backend.** Tự áp dụng migration rồi seed 56 món, 60 nguyên liệu,
28 ngày lịch sử tiêu thụ:

```bash
dotnet run --project backend/QlyCoffee.Api
```

Lần chạy đầu, log in ra:

```
⚠️ Đặt DEFAULT_STORE_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx vào file .env rồi khởi động lại
```

Copy mã đó vào dòng `DEFAULT_STORE_ID` trong `.env`, dừng backend rồi chạy lại.

**Bước 3 — Frontend** (cửa sổ terminal khác):

```bash
dotnet run --project frontend/QlyCoffee.Client
```

| Địa chỉ | Nội dung |
|---|---|
| http://localhost:5180 | Trang bán hàng |
| http://localhost:5180/admin | Trang quản lý |
| http://localhost:5080/swagger | Tài liệu API (27 endpoint) |
| http://localhost:5050 | pgAdmin (chạy `docker compose --profile tools up -d`) |

> **Đổi schema về sau** thì tạo migration mới rồi chạy lại backend:
> ```bash
> dotnet ef migrations add TenThayDoi \
>   --project backend/QlyCoffee.Infrastructure \
>   --startup-project backend/QlyCoffee.Api \
>   --output-dir Persistence/Migrations
> ```
> Muốn xóa sạch làm lại: `DROP DATABASE qly_coffee;` rồi chạy backend.

**Tài khoản mẫu:**

| Vai trò | Email | Mật khẩu |
|---|---|---|
| Chủ quán | `admin@qlycoffee.vn` | `Admin@123456` |
| Nhân viên | `nhanvien@qlycoffee.vn` | `Nhanvien@123` |

> **Muốn xem tính năng AI ngay:** vào `/admin/ke-hoach` → bấm **Tạo kế hoạch ngay**.
> Seed đã cố ý tạo 4 lô cận hạn (trân châu còn 1 ngày, sữa 2 ngày, đào 3 ngày, xoài 4 ngày)
> nên hệ thống sẽ có việc để phân tích ngay lần chạy đầu.
>
> Không có khóa Anthropic cũng chạy được: đặt `AI_ENABLED=false` trong `.env`, hệ thống dùng
> bản diễn giải mẫu — **số liệu và đề xuất vẫn đầy đủ** vì chúng do code tính.

---

## Cấu trúc

```
QlyCoffee.sln
├── .env                          ⭐ TOÀN BỘ cấu hình nằm ở đây (13 nhóm biến)
├── docker-compose.yml
│
├── backend/
│   ├── QlyCoffee.Domain/         Thực thể + enum, comment tiếng Việt chi tiết
│   ├── QlyCoffee.Infrastructure/ DbContext (comment ghi thẳng vào DB) + Seed
│   ├── QlyCoffee.Application/    ⭐ TOÀN BỘ logic nghiệp vụ
│   └── QlyCoffee.Api/            Controllers + Program.cs + tích hợp Claude
│
├── frontend/
│   └── QlyCoffee.Client/         Blazor WebAssembly
│
└── shared/
    └── QlyCoffee.Shared/         DTO dùng chung — đổi tên trường là cả hai phía cùng báo lỗi
```

---

## Ba quyết định kiến trúc quan trọng nhất

### 1. AI chia ba lớp — LLM tuyệt đối không tính toán

```
Lớp 1  Trừ kho FEFO, tính tồn, giá vốn          → C# thuần
Lớp 2  Dự báo EWMA, rủi ro hết hạn, lãi/lỗ      → C# thuần
Lớp 3  Diễn giải, xếp ưu tiên, viết banner      → Claude
```

Mọi trường **số** trong `PlanSuggestion` đều do backend tính. Claude chỉ điền trường **chữ**.
Ba lớp bảo vệ: prompt cấm tính lại số, JSON schema không có trường số nào, và lọc bỏ mọi
`candidateId` mà AI bịa ra. Lỗi AI không bao giờ làm mất bản kế hoạch.

> Vì sao nghiêm ngặt: mô hình ngôn ngữ viết rất thuyết phục ngay cả khi tính sai.
> Báo cáo tài chính sai mà đọc vào thấy hợp lý là thứ nguy hiểm nhất.

### 2. Kho trừ theo FEFO, có khóa hàng và chống ghi trùng

`InventoryService.ConsumeFefoAsync` — hàm quan trọng nhất hệ thống:

- **FEFO** (First Expired First Out) chứ không phải FIFO — lô hết hạn sớm nhất dùng trước.
  Đây chính là thứ làm cho cảnh báo cận hạn có ý nghĩa.
- **`SELECT ... FOR UPDATE`** khóa hàng — không có thì hai thu ngân cùng xác nhận
  sẽ làm tồn kho âm.
- **`idempotency_key` unique** ở tầng database — client retry cũng không trừ hai lần.
- Toàn bộ nằm trong **transaction Serializable**. Thiếu một nguyên liệu thì rollback sạch,
  không có chuyện trừ nửa chừng.

### 3. Kho chỉ bị trừ tại trạng thái `Completed`

```
Pending → Confirmed → Preparing → Ready → Completed
                                              ↑
                                         TRỪ KHO tại đây
```

Trừ lúc `Pending` thì khách bỏ giỏ hàng làm kho bị trừ oan. `Completed` là lúc món **thật sự
đã pha xong và giao đi**, tức là lúc nguyên liệu thật sự rời khỏi kho — nhờ vậy đơn hủy giữa
chừng không cần hoàn kho, vì nó chưa bao giờ bị trừ.

### 4. Sơ chế — bước đứng trước bán hàng

Ngoài đời không ai cân 8g lá trà cho từng ly: người ta ủ một bình 2 lít rồi rót dần cho ba
mươi ly, và bình đó hỏng sau vài tiếng dù còn nguyên. Vì vậy kho có **hai bước**, không phải một:

```
Nhập kho  ──────▶  SƠ CHẾ  ──────▶  Bán hàng
mua 1kg lá         80g lá trà        200ml cốt
hồng trà về        → 2000ml cốt      → một ly trà sữa
                   hạn 6 tiếng
                   ProductionOut     SaleOut
                   + ProductionIn    (FEFO như thường)
```

- **Bán thành phẩm là `Ingredient` bình thường**, chỉ khác cờ `is_prepared`. Nhờ vậy FEFO,
  sổ cái, cảnh báo hạn dùng, giá vốn và phép "còn làm được mấy ly" dùng lại được nguyên vẹn,
  không phải viết đường đi riêng nào.
- **Hạn dùng tính bằng GIỜ** (`prep_recipes.shelf_life_hours`), không phải ngày — cốt trà 6
  tiếng, cà phê phin 12 tiếng, cold brew 7 ngày. Job quét hạn vì thế chạy lại mỗi 15 phút chứ
  không chỉ lúc 08:00.
- **Giá vốn mẻ = tổng giá vốn thật của các lô đã bị trừ** chia cho sản lượng, không lấy giá
  bình quân — mẻ ủ từ lô trà cận hạn giá rẻ phải rẻ đúng như vậy.
- Hết cốt thì món hiện **"Chưa sơ chế Cốt hồng trà"** chứ không phải "Hết hàng": một bên là
  đi ủ mẻ mới, một bên là gọi nhà cung cấp — hai hành động khác nhau.

Chín công thức mẻ có sẵn: 5 loại cốt trà, cà phê phin, cold brew, nước đường, kem muối.
Màn hình ở `/admin/so-che`.

Việc bán quá số lượng trong lúc đang pha được chặn ở chỗ khác: phép kiểm tra tồn kho lấy tồn
thật **trừ đi phần các đơn đang chờ đã chiếm chỗ** (`EnsureIngredientsAvailableAsync`).

Trừ theo **đúng gram** trong công thức, nhân hệ số size và cộng cả nguyên liệu của topping
khách chọn thêm, rồi nhân tỷ lệ hao hụt của từng nguyên liệu. Cả bill được **gộp trước rồi
mới trừ một lần**: hai món cùng dùng sữa thì chỉ sinh một bút toán. Hủy đơn đã trừ thì hoàn
về **đúng những lô** đã bị trừ.

---

## Hết nguyên liệu giữa ca thì làm gì

Quán trà sữa hết trân châu lúc 4 giờ chiều là chuyện hằng ngày. Màn hình quầy
không chỉ báo **"Hết"** — nó trả lời câu hỏi *tiếp theo làm gì*:

| Nhãn trên ô món | Nghĩa | Nhân viên làm gì |
|---|---|---|
| **Nấu ~25′** (màu nhấn) | Nấu thêm nhanh hơn `POS_PREP_QUICK_MINUTES` | Bấm **+**, báo khách chờ 25 phút |
| **Tạm ngưng** (đỏ) | Nấu lâu quá, hoặc phải nhập hàng | Xin lỗi khách. Vẫn bấm **+** được để nấu cho ca sau |
| **Hết** | Hàng chờ pha đã chiếm hết chỗ | Chờ pha xong là bán tiếp |

Nút **+** ngay trên ô món và cạnh topping trong khay chạy **một mẻ sơ chế thật**:
trừ nguyên liệu thô, tạo lô bán thành phẩm mới kèm hạn dùng, ghi bút toán kho —
cùng đường mà trang [Sơ chế](#) đi. Đặt nút ở quầy vì thiếu hàng chỉ lộ ra đúng
lúc có khách đứng trước mặt; bỏ khách chạy sang trang khác là mất cả mạch bán hàng.

Cần nửa mẻ hay ba mẻ thì vào `/admin/so-che` — ở quầy giữa lúc đông khách, thêm
một ô nhập số là thêm một chỗ bấm nhầm.

> **Trân châu và pudding là bán thành phẩm**, không phải hàng nhập: quán nấu từ
> trân châu khô. Vì vậy chúng chỉ vào kho qua màn hình Sơ chế, hạn dùng tính bằng
> **giờ** chứ không phải ngày — trân châu để qua đêm thì cứng, sáng hôm sau nhai
> như hạt sạn.

## Giá bán, giá vốn và thuế GTGT

**Giá vốn không nhập tay.** Nó là số dẫn xuất từ công thức định lượng:

```
giá vốn = Σ (định lượng × đơn giá nguyên liệu × (1 + hao hụt))
```

Toàn bộ công thức của 56 món nằm trong một file duy nhất:
[`backend/QlyCoffee.Infrastructure/Seed/MenuCatalog.cs`](backend/QlyCoffee.Infrastructure/Seed/MenuCatalog.cs).
Mỗi dòng ghi rõ định lượng và quy đổi thực tế — *"2 miếng đào ngâm, mỗi miếng ≈ 30g"*,
*"2 lát cam vàng cả vỏ, mỗi lát ≈ 15g"* — để nhân viên mới đứng quầy đọc là làm được.

**Giá bán thì luôn là quyền của người bán.** Hệ thống chỉ *gợi ý*:

| | Đồ uống | Đồ ăn kèm |
|---|---|---|
| Tỷ lệ giá vốn mục tiêu | 35% | 50% |
| Giá đề nghị | `giá vốn ÷ tỷ lệ`, làm tròn **lên** bội số 1.000đ | |

Đồ ăn để mức cao hơn vì bánh **nhập về nguyên cái** — quán không tạo thêm giá trị bằng tay
nghề pha chế. Trang `/admin/mon` có bộ lọc **Đồ uống / Đồ ăn** riêng vì chung một ngưỡng thì
mọi món bánh đều bị tô cảnh báo oan.

Sửa giá: vào `/admin/mon`, **bấm thẳng vào ô giá**. Hộp thoại hiện giá vốn, giá đề nghị và
biên lợi nhuận cập nhật theo từng phím gõ. Hệ thống **không chặn** giá thấp hơn giá vốn —
quán có quyền bán lỗ một món để kéo khách hoặc xả nguyên liệu cận hạn — nó chỉ nói trước
hậu quả.

**Thuế GTGT.** Mặc định: **giá niêm yết đã bao gồm thuế**, thuế suất **8%**. Tổng tiền khách
trả không đổi, khối thuế dưới nút "Đặt món" chỉ **tách ngược** ra tiền hàng và tiền thuế.
Căn cứ pháp lý của từng chế độ ghi ở đầu
[`shared/QlyCoffee.Shared/Tax.cs`](shared/QlyCoffee.Shared/Tax.cs); đổi chế độ và thuế suất
bằng `STORE_TAX_MODE` / `STORE_VAT_RATE` trong `.env`, không phải sửa mã nguồn.

> ⚠️ Hộ kinh doanh nộp thuế theo **phương pháp trực tiếp** phải đặt `STORE_TAX_MODE=0`.
> Nhóm này dùng hóa đơn bán hàng, trên chứng từ không có dòng thuế GTGT tách riêng.

**Thêm món mới vào quán đang chạy.** Seeder chỉ chạy khi database còn trống. Món thêm vào
`MenuCatalog.cs` sau ngày khai trương đi vào database qua `MenuSync` — bật
`MENU_SYNC_ENABLED=true` rồi khởi động lại. Nó **chỉ thêm phần còn thiếu** và **không bao
giờ ghi đè giá bán** chủ quán đã chỉnh.

---

## Về giao diện

**Màu là dữ liệu, không phải trang trí.** Ba cột trong database điều khiển toàn bộ hình ảnh:

| Cột | Dùng để |
|---|---|
| `ingredients.color_hex` | Vẽ hình minh họa SVG và chấm màu trong bảng kho |
| `products.color_primary_hex` | Màu thân nước trong hình ly |
| `products.color_accent_hex` | Màu lớp kem/foam |

Ba component đọc màu này:

- **`IngredientIcon.razor`** — 22 hình minh họa nguyên liệu vẽ tay bằng SVG (hạt cà phê có
  rãnh giữa, hộp sữa gấp nắp, viên trân châu xếp chồng, quả đào có lá, lát chanh có múi…).
  Mỗi hình tự sinh 3 tông từ 1 màu gốc nên nguyên liệu nhạt như sữa vẫn có ranh giới rõ.
- **`DrinkGlass.razor`** — 4 kiểu ly (ly cao đá, cốc sứ nóng có hơi bốc, ly trà sữa có trân
  châu lắng đáy, ly đá xay nắp vòm), vẽ nước 2 lớp có gợn sóng ở ranh giới.
- **`Icons.cs`** — 24 icon giao diện.

Không phụ thuộc file ảnh ngoài → không có ảnh vỡ, sắc nét ở mọi độ phân giải.

### Ảnh chụp thật

**55/56 món có ảnh chụp RIÊNG**, nằm ở `wwwroot/uploads/products/`.
Món duy nhất chưa có là **Cà phê cốt dừa** — nó mượn ảnh mẫu theo nhóm trong
`wwwroot/img/`, vì kho ảnh CC0 không có tấm cà phê cốt dừa nào dùng được.

Bộ ảnh mẫu theo nhóm (18 tấm) vẫn giữ làm **lớp đỡ**: món mới thêm vào chưa kịp
chụp thì đã có sẵn một tấm đúng nhóm, không bao giờ có ô ảnh trống. Quy tắc ghép
nằm gọn trong `Services/DrinkPhoto.cs` — thứ tự các nhánh ở đó **là một phần của
logic**, đọc chú thích trước khi sắp xếp lại.

| | |
|---|---|
| Nguồn | [StockSnap.io](https://stocksnap.io), tìm qua API [Openverse](https://openverse.org) |
| Giấy phép | **CC0** — dùng thương mại tự do, không cần ghi công tác giả |
| Loại ảnh | Chụp bằng máy ảnh thật, không phải ảnh dựng bằng AI |

Ba món từng phải dùng hình vẽ (**trà sữa trân châu đường đen, trà sữa matcha,
matcha latte**) nay đều đã có ảnh chụp riêng, đúng thứ trong ly.

**Mọi tấm đều được xem tận mắt trước khi gán**, không lấy theo thứ hạng tìm kiếm.
Đợt đầu (22 tấm) loại 4 tấm dính logo Starbucks và một loạt ảnh sai chủ đề; đợt mở
rộng (36 tấm) loại thêm ảnh dính logo McCafé, Tim Hortons, TEASPOON, TruMoo, và một
ly trà ổi in tên quán khác trên lót ly — tấm đó **chỉ lộ ra sau khi cắt vuông**, nên
bước soát phải nhìn ảnh ĐÃ CẮT chứ không phải ảnh gốc.

Ba script tái lập lại quy trình nằm ở [`scripts/`](scripts/):
`tai-ung-vien.sh` → `ghep-bang-anh.ps1` → `gan-anh-da-chon.sh`.

Hình vẽ SVG là **hình tạm theo từng món**. Giao diện luôn ưu tiên ảnh chụp; SVG chỉ
hiện khi món chưa có ảnh, nên có thể chụp và thay dần, không cần làm một lượt.

Vào `/admin/mon` → bấm vào ô hình bên trái tên món. Hai đường vào ảnh:

| Cách | Dùng khi |
|---|---|
| **Tải file** | Ảnh chụp bằng điện thoại tại quán — đường dùng chính |
| **Dán đường dẫn** | Ảnh đã nằm trên CDN hoặc kho ảnh sẵn có |

Bộ lọc **Chưa có ảnh** ở đầu trang cho biết còn bao nhiêu món phải chụp.

**Chụp thế nào cho ăn khớp với thiết kế:** cùng một góc và một phông cho cả menu
(ngang tầm mắt hoặc thẳng từ trên xuống, không trộn lẫn), nền sáng đều, ly đặt
lệch tâm một chút. Ảnh bị cắt thành **khung vuông** nên chừa lề quanh ly. Ảnh chụp
ngược sáng hoặc lệch tông sẽ phá vỡ bảng màu nâu ấm của giao diện — khi đó để
nguyên hình vẽ SVG còn tử tế hơn.

Ảnh lưu ở `backend/QlyCoffee.Api/wwwroot/uploads/products/`, tên file do server tự
sinh. Thay ảnh thì ảnh cũ bị xóa luôn nên thư mục không phình ra.

**Design system** ([design-system.css](frontend/QlyCoffee.Client/wwwroot/css/design-system.css)):
neutrals ám nâu ấm (hue 28–35) thay vì xám chết, một accent duy nhất là màu crema espresso
`hsl(24 72% 42%)`, ba màu ngữ nghĩa chỉ dùng mã hóa trạng thái. Chế độ tối được thiết kế
riêng chứ không đảo ngược chế độ sáng.

**Mức khẩn cấp luôn mã hóa bằng ≥2 cách** (không bao giờ chỉ dùng màu): sọc màu + độ dài
thanh đo + chữ "còn N ngày". Người mù màu và trình đọc màn hình vẫn nhận được thông tin.

---

## Màn hình (21 route, đầy đủ)

**Cửa hàng (công khai)**

| Đường dẫn | Điểm đáng chú ý |
|---|---|
| `/` | Banner khuyến mãi do AI đề xuất đặt ngay dưới hero |
| `/menu` | Đủ 4 trạng thái (tải/rỗng/lỗi/có data). Món tạm hết dồn xuống cuối |
| `/menu/{slug}` | Giá cập nhật ngay khi chọn size/topping. Tồn kho ràng buộc theo **từng size** |
| `/gio-hang` | Gộp giỏ + thông tin đặt món. Đồng bộ lại tồn kho khi vào trang |
| `/don-hang/{code}` | Tự làm mới mỗi 20 giây, không cần đăng nhập |
| `/dang-nhap` | Form riêng, không có thanh điều hướng |

**Quản lý**

| Đường dẫn | Điểm đáng chú ý |
|---|---|
| `/admin` | 4 thẻ số liệu, biểu đồ 7 ngày, kế hoạch AI mới nhất |
| `/admin/ke-hoach` | ⭐ Biểu đồ rủi ro, bảng lãi lỗ 3 dòng, nút Duyệt/Sửa/Bỏ qua |
| `/admin/don-hang` | Xác nhận đơn = trừ kho. Tự làm mới 15s. Hủy đơn bắt buộc lý do |
| `/admin/mon` | Biên lợi nhuận dạng thanh đo, đánh dấu món chưa có công thức |
| `/admin/mon/{id}/cong-thuc` | Giá vốn và biên lợi nhuận tính lại **ngay khi gõ** |
| `/admin/so-che` | Ủ cốt trà, cà phê phin, nấu nước đường. Đếm ngược hạn từng bình theo phút |
| `/admin/nguyen-lieu` | Thanh đo mức tồn (100% = gấp đôi ngưỡng tối thiểu) |
| `/admin/lo-hang` | Sắp FEFO, mã hóa khẩn cấp 3 cách cùng lúc |
| `/admin/nhap-kho` | Nhập theo kg/lít, hệ thống tự quy đổi. Cảnh báo khi đơn giá lệch > 40% |
| `/admin/hao-hut` | Lý do chọn từ danh sách để thống kê được, kèm ô mô tả tự do |
| `/admin/khuyen-mai` | Nhãn "Do AI đề xuất" + doanh thu thực tế — thước đo chất lượng AI |
| `/admin/bao-cao` | Tách riêng hao hụt **do quá hạn** — con số mà kế hoạch AI cần kéo xuống |

> Danh mục món (5 nhóm) chưa có màn hình quản lý riêng vì hiếm khi thay đổi.
> Sửa qua pgAdmin, hoặc thêm trang CRUD sau nếu cần.

---

## Tài liệu

| File | Nội dung |
|---|---|
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | Đặc tả kỹ thuật đầy đủ: schema, quy tắc nghiệp vụ, thuật toán AI, 10 lỗi hay gặp |
| [`docs/`](docs/) | Kế hoạch chiến lược ban đầu (định vị, thị trường, SEO). Lưu ý: lõi AI trong `docs/03` đã đổi — xem `ARCHITECTURE.md` §00.1 |

---

## Trước khi lên production

- [ ] Đổi `JWT_SECRET` (`openssl rand -base64 48`)
- [ ] Đổi `DB_PASSWORD`, `CRON_SECRET`
- [ ] Đặt `ANTHROPIC_API_KEY` thật
- [ ] Đặt `SEED_ENABLED=false`
- [ ] Bật backup tự động cho PostgreSQL
- [ ] Thêm trang chính sách bảo mật (Nghị định 13/2023 — có lưu SĐT khách)
