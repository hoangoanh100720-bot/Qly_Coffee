# 07 — SEO: những gì đã làm ở frontend và những gì còn thiếu

> Tài liệu này ghi lại phần SEO **đã được cài đặt thật trong mã nguồn** frontend.
> Chiến lược tăng trưởng tổng thể nằm ở [05-seo-va-tang-truong.md](05-seo-va-tang-truong.md);
> file này chỉ nói về kỹ thuật.

---

## 1. Giới hạn gốc rễ: Blazor WebAssembly không có HTML sẵn

Đây là điều quan trọng nhất phải hiểu trước khi đọc phần còn lại.

Khi một bot yêu cầu `https://qlycoffee.vn/menu`, máy chủ trả về **đúng file
`wwwroot/index.html`** — không có tên món nào, không có giá nào. Toàn bộ nội dung
được JavaScript dựng ra sau khi tải xong gói WebAssembly.

Hệ quả chia làm hai nhóm bot:

| Bot | Chạy JavaScript? | Nhìn thấy gì |
|---|---|---|
| Googlebot | Có, nhưng xếp hàng đợi dựng lại | Nội dung đầy đủ, chậm hơn vài ngày |
| Bingbot | Có, hạn chế hơn | Thường đầy đủ |
| Facebook / Zalo / Messenger | **Không** | Chỉ `index.html` |
| Twitter/X card bot | **Không** | Chỉ `index.html` |
| Cốc Cốc, các bot nhỏ | Thường **không** | Chỉ `index.html` |

**Cách xử lý đã áp dụng:** `index.html` chứa sẵn một bộ thẻ meta + JSON-LD đầy đủ
cho toàn site. Bot không chạy JavaScript vẫn có tiêu đề, mô tả, ảnh xem trước,
địa chỉ và giờ mở cửa. Bot có chạy JavaScript thì nhận bộ thẻ riêng của từng trang.

---

## 2. Những gì đã cài đặt

### 2.1 `Components/Common/SeoHead.razor`

Component dùng chung, sinh ra cho mỗi trang: `<title>`, `meta description`,
`link canonical`, `meta robots`, bộ Open Graph và Twitter Card.

```razor
<SeoHead Title="Thực đơn"
         Description="Toàn bộ thực đơn Qly Coffee..."
         Path="/menu"
         Image="img/drink-tra-sua.webp" />
```

Đã gắn vào: trang chủ, thực đơn (kể cả từng danh mục), chi tiết món, giỏ hàng,
tra cứu đơn, đăng nhập.

**Chi tiết dễ bỏ sót — thẻ trùng lặp.** `HeadOutlet` của Blazor **thêm** thẻ chứ
không thay thế. Không xử lý gì thì mỗi trang có hai thẻ `canonical` trỏ hai nơi
khác nhau, và Google gặp hai canonical mâu thuẫn thì **bỏ qua cả hai**.
Cách giải quyết: các thẻ mặc định trong `index.html` mang thuộc tính
`data-seo-default`, và `SeoHead.OnAfterRenderAsync` gọi `window.qlySeo.clearDefaults()`
để gỡ chúng — **sau** khi thẻ của trang đã được ghi, nên không có khoảnh khắc nào
`<head>` thiếu thẻ mô tả.

### 2.2 Trang nào được lập chỉ mục

| Đường dẫn | robots | Lý do |
|---|---|---|
| `/` | index, follow | Trang chính |
| `/menu` | index, follow | Nội dung giá trị nhất |
| `/menu/danh-muc/{slug}` | index, follow | Mỗi danh mục là một trang riêng |
| `/menu/{slug}` | index, follow | Trang mang nhiều truy cập tự nhiên nhất |
| `/tra-cuu-don` | index, follow | Trang công cụ công khai |
| `/don-hang/{ma}` | **noindex**, follow | Chứa đơn của một người cụ thể |
| `/gio-hang`, `/dat-mon` | **noindex**, follow | Riêng tư, nội dung mỏng |
| `/dang-nhap` | **noindex**, follow | Không có lý do lên Google |
| `/admin/**` | **noindex, nofollow** | Khai báo một lần ở `AdminLayout.razor` |

Khối quản lý được chặn **hai lớp**: `Disallow: /admin` trong `robots.txt` ngăn bot
truy cập, còn thẻ `meta robots` ngăn trang xuất hiện trên kết quả kể cả khi bot
tìm ra đường dẫn từ nơi khác. Cần cả hai vì `robots.txt` một mình không ngăn được
việc lập chỉ mục.

### 2.3 Mô tả riêng cho từng trang

Nội dung trùng lặp bị Google hạ điểm. Vì vậy:

- **Chi tiết món** — `ProductDetail.BuildSeoDescription()` ghép mô tả trong
  database với giá hiện tại. Mỗi món một câu khác nhau, có kèm giá (đoạn mô tả
  có giá được bấm nhiều hơn hẳn).
- **Danh mục** — `Menu.SeoDescription` đổi theo danh mục đang lọc. `h1` trên trang
  cũng đổi theo và **khớp với thẻ title** — đây là tín hiệu xếp hạng cơ bản.

### 2.4 Dữ liệu có cấu trúc (JSON-LD)

Trong `index.html`, hai khối:

- `CafeOrCoffeeShop` — tên, mô tả, địa chỉ, điện thoại, giờ mở cửa, khoảng giá,
  liên kết tới thực đơn, hành động đặt món. Google dùng khối này cho Knowledge
  Panel và kết quả tìm kiếm địa phương.
- `WebSite` + `SearchAction` — ô tìm kiếm hiện ngay dưới kết quả cho tên thương hiệu.

Chân trang (`ShopLayout.razor`) lặp lại thông tin này dưới dạng **microdata**
(`itemscope`/`itemprop`). Google đối chiếu hai nguồn; trùng khớp thì độ tin cậy tăng.

> ⚠️ Ba nơi phải khớp nhau tuyệt đối: hằng số trong `ShopLayout.razor`, khối JSON-LD
> trong `index.html`, và hồ sơ Google Business Profile của quán. Lệch nhau là lỗi
> SEO địa phương phổ biến nhất.

### 2.5 Ảnh

Toàn bộ khối bán hàng dùng **ảnh chụp thật** (`wwwroot/img/`), không còn hình vẽ
vector dựng động. Ba lý do đều liên quan tới SEO và bán hàng:

- Google Image Search không đọc được SVG dựng bằng JavaScript. Ngành đồ uống có
  lượng truy cập đáng kể từ tìm kiếm hình ảnh.
- `og:image` bắt buộc là ảnh bitmap — bot mạng xã hội không dựng được SVG.
- Ảnh thật bán được hàng.

Kỹ thuật đi kèm:

- Mọi `<img>` đều có `width`/`height` → trình duyệt giữ chỗ trước, lưới không
  giật khi ảnh về. Đây là chỉ số **CLS** trong Core Web Vitals.
- Ảnh hero có `fetchpriority="high"` và được `preload` trong `index.html` → nó là
  phần tử **LCP**, cần tải song song với WebAssembly thay vì đợi Blazor khởi động.
- Ảnh ngoài màn hình đầu tiên dùng `loading="lazy"`.
- `alt` được sinh bởi `DrinkPhoto.AltFor()` — mô tả đầy đủ, phân biệt ảnh thật của
  quán với ảnh minh họa.

### 2.6 File tĩnh

| File | Nội dung |
|---|---|
| `wwwroot/robots.txt` | Cho phép nội dung công khai, chặn `/admin`, `/gio-hang`, `/dang-nhap`, `/don-hang/`, `_framework/` |
| `wwwroot/sitemap.xml` | Ba trang tĩnh + ảnh trang chủ |
| `wwwroot/manifest.json` | Tên, mô tả, màu chủ đề đúng theo bảng màu mới, hai shortcut |

`index.html` cũng có khối `<noscript>` chứa tên quán, mô tả, địa chỉ, giờ mở cửa
và số điện thoại dưới dạng HTML thật — đây là nội dung duy nhất bot không chạy
JavaScript đọc được ngoài các thẻ meta.

---

## 3. Việc bắt buộc phải làm khi deploy

### 3.1 Đổi tên miền

Chuỗi `https://qlycoffee.vn` đang là **giá trị tạm**. Tìm-thay trong ba file:

```
frontend/QlyCoffee.Client/wwwroot/index.html    (thẻ meta + hai khối JSON-LD)
frontend/QlyCoffee.Client/wwwroot/robots.txt    (dòng Sitemap)
frontend/QlyCoffee.Client/wwwroot/sitemap.xml   (mọi thẻ <loc> và <image:loc>)
```

Component `SeoHead` **không** cần sửa — nó lấy tên miền từ `NavigationManager.BaseUri`
lúc chạy nên tự đúng.

### 3.2 Đổi thông tin quán thật

Địa chỉ, điện thoại, email trong `ShopLayout.razor` và trong JSON-LD của `index.html`.

### 3.3 Máy chủ phải trả `index.html` cho mọi đường dẫn

Đây là ứng dụng một trang. Nếu máy chủ trả 404 cho `/menu` thì Google sẽ loại
toàn bộ trang con khỏi chỉ mục. Cấu hình fallback tới `index.html` cho mọi route
không khớp file tĩnh.

### 3.4 Gửi sitemap

Google Search Console → Sitemaps → nhập `sitemap.xml`.

---

## 4. Còn thiếu — xếp theo mức tác động

### 4.1 Prerender phía máy chủ ⭐ tác động lớn nhất

Đây là **giới hạn thật sự** của SEO hiện tại, mọi thứ khác chỉ là bù đắp.

Ba hướng, từ nhẹ tới nặng:

1. **Đặt một lớp prerender trước site** (Prerender.io, hoặc một hàm serverless
   dùng Playwright): phát hiện user-agent là bot thì trả HTML đã dựng sẵn.
   Không phải sửa mã nguồn. Nhanh nhất để triển khai.
2. **Chuyển sang Blazor Web App** với chế độ `InteractiveWebAssembly` và bật
   prerendering. Máy chủ dựng HTML lần đầu rồi WebAssembly tiếp quản. Đây là cách
   Microsoft khuyến nghị, nhưng phải tái cấu trúc dự án.
3. **Tách một trang marketing tĩnh riêng** cho `/` và `/menu`, giữ Blazor cho
   phần đặt món. Tốn công duy trì hai nơi.

### 4.2 Sitemap động cho từng món

`sitemap.xml` hiện chỉ có ba trang tĩnh. Danh sách món nằm trong database và thay
đổi liên tục, frontend tĩnh không sinh được.

Cần một endpoint ở backend, ví dụ `GET /sitemap-mon.xml`, đọc bảng `products` rồi
trả XML; sau đó thêm một dòng `Sitemap:` nữa vào `robots.txt`.

### 4.3 JSON-LD `Product` cho từng món

Trang chi tiết món nên có khối `Product` với `offers` (giá, `priceCurrency: VND`,
`availability`). Google sẽ hiện giá ngay trên kết quả tìm kiếm.

Chưa làm vì cần đặt bên trong component Blazor, mà bot không chạy JavaScript thì
không thấy — nên nó chỉ thật sự có tác dụng sau khi có prerender (mục 4.1).
Làm trước cũng không sai, chỉ là chưa thu được lợi ích.

### 4.4 Breadcrumb có đánh dấu

Trang chi tiết món đã có breadcrumb dạng chữ. Bọc thêm `BreadcrumbList` JSON-LD
thì Google hiện đường dẫn phân cấp thay vì URL thô.

### 4.5 Nội dung dài dạng bài viết

Trang bán hàng thuần túy khó xếp hạng cho các truy vấn thông tin
("cà phê phin khác gì espresso", "trà sữa bao nhiêu calo"). Một khu vực blog sẽ
mở ra nhóm truy vấn này — nhưng đó là việc của nội dung, không phải của mã nguồn.

---

## 5. Cách tự kiểm tra

```bash
# Xem HTML mà bot KHÔNG chạy JavaScript nhận được
curl -s https://qlycoffee.vn/menu | grep -i "<title>\|description\|og:"

# robots.txt và sitemap có phục vụ được không
curl -sI https://qlycoffee.vn/robots.txt
curl -s  https://qlycoffee.vn/sitemap.xml | head -20
```

Công cụ nên dùng:

- **Google Search Console → Kiểm tra URL → Kiểm tra trang thực tế** — cho biết
  Googlebot thật sự thấy gì sau khi chạy JavaScript.
- **Facebook Sharing Debugger** — kiểm tra khung xem trước khi dán link.
  Đây là nơi lộ rõ nhất giới hạn ở mục 1.
- **PageSpeed Insights** — đo LCP và CLS trên thiết bị di động.
- **Rich Results Test** — kiểm tra hai khối JSON-LD có hợp lệ không.
