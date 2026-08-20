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

**Bước 2 — Backend.** Tự áp dụng migration rồi seed 22 món, 29 nguyên liệu,
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

### 3. Kho chỉ bị trừ tại trạng thái `Confirmed`

```
Pending → Confirmed → Preparing → Ready → Completed
             ↑
        TRỪ KHO tại đây
```

Trừ lúc `Pending` thì khách bỏ giỏ hàng làm kho bị trừ oan. Trừ lúc `Completed` thì trong
lúc đang pha hệ thống vẫn tưởng còn hàng nên bán quá số lượng. `Confirmed` là đúng lúc quán
cam kết làm món. Hủy đơn đã trừ thì hoàn về **đúng những lô** đã bị trừ.

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

**19/22 món đã có ảnh chụp thật**, tải sẵn về `wwwroot/uploads/products/`.

| | |
|---|---|
| Nguồn | [StockSnap.io](https://stocksnap.io), tìm qua API [Openverse](https://openverse.org) |
| Giấy phép | **CC0** — dùng thương mại tự do, không cần ghi công tác giả |
| Loại ảnh | Chụp bằng máy ảnh thật, không phải ảnh dựng bằng AI |

Ba món còn dùng hình vẽ: **trà sữa trân châu đường đen, trà sữa matcha, matcha latte**.
Kho ảnh CC0 miễn phí không có ảnh trà sữa trân châu và matcha latte nào dùng được —
kết quả trả về toàn ảnh sai món hoặc ảnh quảng cáo có in số điện thoại. Ảnh sai món
tệ hơn hình vẽ, nên để nguyên hình vẽ và chờ ảnh chụp thật của quán.

Toàn bộ 22 tấm đều được **xem tận mắt trước khi gán**, không chọn theo thứ hạng tìm
kiếm. Lượt chọn tự động đầu tiên đã loại được 4 tấm dính logo Starbucks và một loạt
ảnh sai chủ đề (laptop, chai bia, bàn ăn sáng) — nhưng vẫn lọt lưới 3 tấm, phải soát
tay mới phát hiện.

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
