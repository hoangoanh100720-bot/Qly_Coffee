# 08 — Đưa hệ thống lên máy chủ Windows + IIS bằng WinSCP

Tài liệu này hướng dẫn deploy lần đầu và cập nhật các lần sau. Làm theo đúng
thứ tự; mỗi bước đều có cách kiểm chứng ngay tại chỗ, nên nếu sai thì biết sai
ở đâu chứ không phải dò ngược từ đầu.

---

## 1. Ba phần độc lập

Từ bản tách dự án, hệ thống lên máy chủ thành **ba thứ riêng biệt**:

| Phần | Nội dung | Là gì trên IIS |
|---|---|---|
| `deploy/api` | Backend .NET 8 + PostgreSQL | Ứng dụng .NET (cần Hosting Bundle) |
| `deploy/web` | Trang bán hàng — công khai, cần SEO | Site tĩnh |
| `deploy/admin` | App quản lý — PWA, cài được lên máy | Site tĩnh |

Ba phần không dùng chung file nào lúc chạy. Hệ quả thực tế: sửa giao diện trang
bán hàng thì chỉ upload lại `deploy/web`, backend không phải khởi động lại và
nhân viên đang bán hàng không bị gián đoạn một giây nào.

**Vì sao tách đôi frontend?** Trang bán hàng là thứ Google đọc và khách tải về.
Gộp chung, mỗi khách vào xem thực đơn phải tải luôn toàn bộ mã của 14 màn hình
quản lý mà họ không bao giờ mở — chậm trang, và tốc độ trang là một yếu tố xếp
hạng tìm kiếm. Tách ra còn có nghĩa mã nội bộ không nằm trong tay người lạ.

### Tên miền đề xuất

| Phần | Tên miền |
|---|---|
| Trang bán hàng | `motchutcoffee.vn` |
| App quản lý | `quanly.motchutcoffee.vn` |
| Backend API | `api.motchutcoffee.vn` |

Ba tên miền con, ba site IIS. Tách được thì nên tách: chứng chỉ HTTPS, nhật ký
và quyền truy cập đều rạch ròi.

---

## 2. Chuẩn bị máy chủ

Làm một lần duy nhất. Chưa xong phần này thì đừng upload gì cả.

### 2.1. Bật IIS

Server Manager → **Add Roles and Features** → **Web Server (IIS)**.

Trong danh sách thành phần, bật thêm:

- **Web Server → Common HTTP Features → Static Content** (mặc định đã bật)
- **Web Server → Performance → Dynamic Content Compression**
- **Management Tools → IIS Management Console**

### 2.2. Cài .NET 8 **Hosting Bundle**

Tải tại <https://dotnet.microsoft.com/download/dotnet/8.0>, mục **Hosting Bundle**.

> ⚠️ Đây **không** phải ".NET Runtime" thông thường. Hosting Bundle cài thêm
> module `AspNetCoreModuleV2` — thứ thực sự nối IIS với ứng dụng .NET. Cài nhầm
> bản Runtime thường thì IIS báo lỗi `500.19` hoặc `500.21` và không có cách nào
> chạy được.

Cài xong **phải khởi động lại IIS**:

```powershell
net stop was /y
net start w3svc
```

Kiểm chứng:

```powershell
dotnet --list-runtimes
```

Phải thấy dòng có `Microsoft.AspNetCore.App 8.0.x`.

### 2.3. Cài module URL Rewrite

Tải tại <https://www.iis.net/downloads/microsoft/url-rewrite>.

Module này **không đi kèm IIS**. Cả hai app frontend đều cần nó để định tuyến
kiểu một-trang hoạt động (xem mục 7.2 để hiểu vì sao). Thiếu nó thì IIS trả lỗi
`500.19` ngay khi mở site.

### 2.4. PostgreSQL

Bạn đã có sẵn. Chỉ cần chuẩn bị:

- Một database rỗng, **encoding UTF8** (bắt buộc, để tiếng Việt có dấu không hỏng)
- Một user có quyền tạo bảng trên database đó

Không cần chạy file SQL nào. Lần đầu khởi động, backend tự áp dụng toàn bộ
migration để dựng bảng — xem `Program.cs`, đoạn `db.Database.MigrateAsync()`.

### 2.5. Bật SFTP để WinSCP kết nối được

Windows Server không có sẵn SFTP. Cách gọn nhất là bật OpenSSH Server:

```powershell
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Start-Service sshd
Set-Service -Name sshd -StartupType Automatic
New-NetFirewallRule -Name sshd -DisplayName "OpenSSH Server (sshd)" `
    -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22
```

Sau đó trong WinSCP chọn giao thức **SFTP**, cổng 22, đăng nhập bằng tài khoản
Windows của máy chủ.

> Dùng FTP thường cũng được, nhưng FTP gửi mật khẩu dạng văn bản trần. Nếu buộc
> phải dùng thì chọn **FTPS** chứ đừng chọn FTP.

---

## 3. Dựng gói trên máy của bạn

### 3.1. Sửa `.env` cho môi trường thật

Mở `.env` ở gốc dự án và sửa các dòng sau cho đúng tên miền thật:

```ini
CLIENT_API_URL=https://api.motchutcoffee.vn
ADMIN_SHOP_URL=https://motchutcoffee.vn

CORS_ALLOWED_ORIGINS=https://motchutcoffee.vn,https://quanly.motchutcoffee.vn
```

> ⚠️ `CORS_ALLOWED_ORIGINS` phải có **cả hai** tên miền frontend. Thiếu tên miền
> app quản lý thì trình duyệt chặn mọi lời gọi từ nó, và triệu chứng rất dễ chẩn
> đoán nhầm: màn hình đăng nhập báo "không kết nối được máy chủ" trong khi
> backend vẫn chạy tốt và gọi bằng `curl` vẫn ra kết quả. Lỗi nằm ở trình duyệt,
> không ở mạng.

### 3.2. Chạy script dựng gói

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-deploy.ps1
```

Xong sẽ có thư mục `deploy/` với ba thư mục con. Script tự cảnh báo nếu địa chỉ
vẫn còn là `localhost`.

### 3.3. Sửa `deploy/api/.env` — **bước quan trọng nhất**

Script vừa chép `.env` của máy bạn sang. Nó đang chứa cấu hình dev. Mở
`deploy/api/.env` và sửa:

```ini
ASPNETCORE_ENVIRONMENT=Production

DB_HOST=localhost
DB_NAME=qly_coffee
DB_USER=<user thật>
DB_PASSWORD=<mật khẩu thật>

JWT_SECRET=<khóa mới, tối thiểu 32 ký tự>
```

Sinh khóa JWT mới:

```powershell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Max 256 }))
```

> **Vì sao bắt buộc đổi `JWT_SECRET`?** Khóa này là thứ duy nhất chứng minh một
> token đăng nhập là thật. Ai biết khóa thì tự ký được token với vai trò `Owner`
> — không cần phá mật khẩu của ai, và nhật ký cũng không ghi nhận gì bất thường.
> Khóa trong `.env` của repo là giá trị mẫu, coi như đã công khai.

Kiểm tra lần cuối: `SEED_ENABLED` nên để `false` sau lần chạy đầu tiên, nếu
không mỗi lần khởi động lại nạp dữ liệu mẫu đè lên dữ liệu thật.

---

## 4. Upload bằng WinSCP

Kết nối SFTP tới máy chủ, rồi kéo thả theo bảng:

| Kéo từ (máy bạn) | Thả vào (máy chủ) |
|---|---|
| **nội dung** của `deploy\api\` | `C:\inetpub\qly\api\` |
| **nội dung** của `deploy\web\` | `C:\inetpub\qly\web\` |
| **nội dung** của `deploy\admin\` | `C:\inetpub\qly\admin\` |

> ⚠️ Kéo **nội dung bên trong** thư mục, không kéo bản thân thư mục. Kéo nhầm
> thì trên máy chủ thành `C:\inetpub\qly\web\web\index.html`, IIS không thấy
> `index.html` ở gốc và trả 403.

### Cài đặt WinSCP nên bật

Trong WinSCP, vào **Options → Preferences → Transfer → Edit**:

- **Transfer mode: Binary.** Để `Text`/`Automatic` thì WinSCP đổi ký tự xuống
  dòng trong file mà nó đoán là văn bản. File `.wasm` và `.dat` bị đổi một byte
  là hỏng hẳn mã băm, app trắng trang ngay khi khởi động.
- Bật **"Preserve timestamp"** — giúp IIS trả mã `304` đúng cho file không đổi.

Upload lần đầu khoảng 56 MB, chủ yếu là runtime .NET trong `_framework`.

---

## 5. Tạo site trong IIS

### 5.1. Backend API

**Application Pools → Add Application Pool**

| Mục | Giá trị |
|---|---|
| Name | `qly-api` |
| .NET CLR version | **No Managed Code** |
| Managed pipeline mode | Integrated |

> **"No Managed Code" nghe như sai nhưng là đúng.** Mục đó nói về .NET Framework
> cũ. Ứng dụng .NET 8 chạy trong tiến trình riêng do `AspNetCoreModuleV2` quản
> lý, không dùng CLR của IIS. Chọn `v4.0` thì IIS nạp thừa một runtime không ai
> dùng.

**Sites → Add Website**

| Mục | Giá trị |
|---|---|
| Site name | `qly-api` |
| Application pool | `qly-api` |
| Physical path | `C:\inetpub\qly\api` |
| Host name | `api.motchutcoffee.vn` |

### 5.2. Trang bán hàng

**Add Website** — application pool để mặc định cũng được (site tĩnh, không chạy
mã .NET nào trên máy chủ).

| Mục | Giá trị |
|---|---|
| Site name | `qly-web` |
| Physical path | `C:\inetpub\qly\web` |
| Host name | `motchutcoffee.vn` |

### 5.3. App quản lý

| Mục | Giá trị |
|---|---|
| Site name | `qly-admin` |
| Physical path | `C:\inetpub\qly\admin` |
| Host name | `quanly.motchutcoffee.vn` |

> Nếu bạn đặt app quản lý ở **thư mục con** thay vì tên miền con (ví dụ
> `motchutcoffee.vn/quanly`), phải sửa `<base href="/" />` trong
> `admin/index.html` thành `<base href="/quanly/" />`. Không sửa thì app tải
> được trang đầu rồi chết ở mọi đường dẫn khác.

---

## 6. Phân quyền thư mục

Backend cần **quyền ghi** ở hai chỗ. Thiếu quyền thì ứng dụng khởi động rồi chết
ngay, và nhật ký ghi lỗi đó lại nằm đúng thư mục nó không ghi được.

```powershell
# Nhật ký của Serilog
icacls "C:\inetpub\qly\api\logs" /grant "IIS AppPool\qly-api:(OI)(CI)M" /T

# Ảnh món do nhân viên tải lên
icacls "C:\inetpub\qly\api\wwwroot\uploads" /grant "IIS AppPool\qly-api:(OI)(CI)M" /T
```

Hai site tĩnh chỉ cần quyền đọc — mặc định đã có.

---

## 7. Kiểm tra sau khi deploy

Làm lần lượt. Mỗi bước sau phụ thuộc bước trước, nên sai ở đâu thì dừng ở đó.

### 7.1. Backend sống chưa

```powershell
curl https://api.motchutcoffee.vn/api/menu
```

Ra JSON là xong. Ra lỗi thì xem `C:\inetpub\qly\api\logs\qly-*.log`.

Log rỗng hoặc không có file nào → ứng dụng chết trước cả khi kịp ghi log. Bật
nhật ký thô của IIS: mở `api\web.config`, đổi `stdoutLogEnabled="false"` thành
`"true"`, gọi lại API, rồi đọc `api\logs\stdout_*.log`. **Nhớ đổi lại `false`
sau khi xong** — để `true` thì file log phình mãi không giới hạn.

### 7.2. Định tuyến một-trang có chạy không

Mở thẳng `https://motchutcoffee.vn/menu` (gõ địa chỉ, không phải bấm từ trang chủ).

- Hiện thực đơn → đúng.
- Lỗi 404 → module URL Rewrite chưa cài, hoặc `web.config` chưa được upload.

> `/menu` không phải một file trên đĩa — nó là đường dẫn do Blazor xử lý sau khi
> ứng dụng đã chạy. Quy tắc rewrite trong `web.config` nói với IIS: đường dẫn nào
> không trỏ tới file có thật thì trả về `index.html`. Thiếu nó thì trang chủ vẫn
> chạy nhưng mọi đường dẫn khác đều 404 — kể cả khi Google vào thẳng một địa chỉ
> trong sitemap.

### 7.3. App quản lý có cài được không

Mở `https://quanly.motchutcoffee.vn` bằng Chrome hoặc Edge. Trên thanh địa chỉ
phải xuất hiện biểu tượng **cài đặt**.

Không thấy? Mở DevTools (F12) → tab **Application** → **Manifest**:

| Triệu chứng | Nguyên nhân |
|---|---|
| Báo không tải được manifest | IIS chưa có MIME cho `.webmanifest` → kiểm tra `web.config` đã lên chưa |
| Manifest đọc được nhưng vẫn không có nút cài | Trang đang chạy HTTP. **Trình duyệt chỉ cho cài app qua HTTPS** (trừ `localhost`) |
| Service worker báo lỗi | Xem tab **Application → Service Workers** |

### 7.4. SEO

```
https://motchutcoffee.vn/robots.txt
https://motchutcoffee.vn/sitemap.xml
```

Cả hai phải mở được. Sau đó sửa tên miền `motchutcoffee.vn` trong ba file nếu
tên miền thật khác: `index.html`, `robots.txt`, `sitemap.xml`.

Kiểm tra app quản lý **không** bị lập chỉ mục:

```powershell
curl -I https://quanly.motchutcoffee.vn
```

Phải thấy header `X-Robots-Tag: noindex, nofollow, noarchive`.

---

## 8. HTTPS

Bắt buộc, không phải tuỳ chọn. Ba lý do, theo thứ tự nghiêm trọng:

1. **Mật khẩu nhân viên đi qua mạng dạng trần nếu chạy HTTP.**
2. **App quản lý không cài được nếu không có HTTPS.** Trình duyệt chỉ cho cài
   PWA trên kết nối an toàn.
3. Google xếp hạng HTTPS cao hơn.

Cách gọn nhất trên Windows là **win-acme** (<https://www.win-acme.com>) — miễn
phí, tự gia hạn, tự gắn chứng chỉ vào site IIS:

```powershell
.\wacs.exe
```

Chọn `N` (tạo chứng chỉ mới) → chọn site trong danh sách → làm theo hướng dẫn.
Lặp lại cho cả ba site.

Sau đó thêm chuyển hướng HTTP → HTTPS cho mỗi site (IIS Manager → chọn site →
URL Rewrite → Add Rule → Blank rule):

```xml
<rule name="HTTPS redirect" stopProcessing="true">
  <match url="(.*)" />
  <conditions>
    <add input="{HTTPS}" pattern="^OFF$" />
  </conditions>
  <action type="Redirect" url="https://{HTTP_HOST}/{R:1}" redirectType="Permanent" />
</rule>
```

---

## 9. Cập nhật những lần sau

### Chỉ sửa giao diện (không đụng backend)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-deploy.ps1 -SkipApi
```

Upload đè `deploy\web\` và/hoặc `deploy\admin\`. Không cần dừng gì cả.

### Có sửa backend

1. Trong IIS Manager, **Stop** application pool `qly-api`.

   > Bước này bắt buộc: Windows khóa file `.dll` đang chạy, WinSCP sẽ báo
   > "access denied" giữa chừng và bạn còn lại một bản deploy nửa cũ nửa mới —
   > tình huống tệ hơn cả việc không cập nhật.

2. Upload đè `deploy\api\`.
3. **Start** application pool.

> ⚠️ Cẩn thận với hai thứ trên máy chủ khi upload đè:
>
> - **`.env`** — bản trên máy chủ có mật khẩu thật. Bản trong `deploy/api/` là
>   bản bạn vừa sửa ở mục 3.3. Nếu không chắc, bỏ chọn file này trong WinSCP.
> - **`wwwroot/uploads/`** — ảnh món nhân viên đã tải lên. Trong WinSCP đặt chế
>   độ ghi đè là **"Update"** thay vì "Overwrite" để giữ file chỉ có ở máy chủ.

### Người dùng có phải làm gì không

Không. `web.config` đã đặt `no-cache` cho `index.html`, `appsettings.json` và
`service-worker.js`, nên lần mở tiếp theo trình duyệt tự nhận bản mới. Với app
quản lý đã cài, service worker phát hiện bản mới rồi tự cập nhật ở lần mở sau.

---

## 10. Xử lý sự cố

| Triệu chứng | Nguyên nhân thường gặp |
|---|---|
| `500.19` khi mở site | Thiếu module URL Rewrite, hoặc `web.config` sai cú pháp |
| `500.21` / `500.30` ở API | Thiếu .NET 8 **Hosting Bundle** (cài nhầm Runtime thường) |
| `502.5` ở API | Ứng dụng khởi động rồi chết — gần như luôn là sai chuỗi kết nối database. Xem `logs\stdout_*.log` |
| Trang trắng, F12 báo 404 file `.wasm` | Thiếu MIME `.wasm` → `web.config` chưa upload |
| Trang trắng, báo lỗi integrity | Upload ở chế độ Text làm hỏng file nhị phân → bật **Binary** rồi upload lại |
| Trang chủ chạy, `/menu` lỗi 404 | Chưa cài URL Rewrite (mục 2.3) |
| Đăng nhập báo "không kết nối được máy chủ" | Thiếu tên miền app quản lý trong `CORS_ALLOWED_ORIGINS` |
| Ảnh món không hiện | Sai `CLIENT_API_URL`, hoặc thiếu MIME `.webp` |
| Không tải được ảnh lên | Thiếu quyền ghi `wwwroot\uploads` (mục 6) |
| App quản lý không có nút cài | Đang chạy HTTP, hoặc thiếu MIME `.webmanifest` |
| Upload xong vẫn thấy bản cũ | Ctrl+F5 một lần. Còn nữa thì kiểm tra `web.config` đã lên chưa |

### Chỗ cần nhìn đầu tiên khi bí

1. `C:\inetpub\qly\api\logs\qly-*.log` — nhật ký ứng dụng
2. `C:\inetpub\qly\api\logs\stdout_*.log` — chỉ có khi bật `stdoutLogEnabled`
3. **Event Viewer → Windows Logs → Application** — lỗi ở tầng IIS, trước khi
   ứng dụng kịp chạy
4. **F12 → tab Network** trong trình duyệt — file nào đỏ thì đó là đầu mối
