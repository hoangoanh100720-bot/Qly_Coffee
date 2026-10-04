using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using QlyCoffee.Client;
using QlyCoffee.Client.Services;

// ==============================================================================
//  ĐIỂM KHỞI ĐỘNG CỦA TRANG BÁN HÀNG (Blazor WebAssembly)
//
//  Địa chỉ backend KHÔNG viết cứng ở đây. Nó nằm trong wwwroot/appsettings.json,
//  file này được sinh ra từ biến CLIENT_API_URL trong .env lúc build
//  (xem script scripts/gen-client-config.ps1). Nhờ vậy đổi máy chủ chỉ cần sửa
//  .env, không phải build lại mã nguồn.
//
//  Ở đây KHÔNG đăng ký dịch vụ xác thực nào. Trang bán hàng không có đăng nhập —
//  phần đó thuộc về app quản lý (frontend/QlyCoffee.Admin).
// ==============================================================================

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// --- Địa chỉ backend --------------------------------------------------------
// ApiAddress tự đổi host của địa chỉ cấu hình thành host mà trình duyệt đang
// mở, KHI cả hai đều là máy trong mạng nội bộ. Không có bước này thì địa chỉ
// IP mà script chạy LAN ghi vào appsettings.json sẽ chết ngay lần đổi wifi kế
// tiếp, và cả trang trắng dữ liệu mà không nói được vì sao. Cấu hình trỏ ra
// tên miền thật thì giữ nguyên. Xem QlyCoffee.Ui/Services/ApiAddress.cs.
var apiBaseUrl = ApiAddress.Resolve(
    builder.Configuration["ApiBaseUrl"],
    builder.HostEnvironment.BaseAddress);

// Ảnh món lưu ở backend nên cần biết địa chỉ backend để ghép đường dẫn
Media.ApiBaseUrl = apiBaseUrl;

// --- Liên kết sang app quản lý ----------------------------------------------
// Hai ứng dụng có thể ở hai tên miền, nên đây là cấu hình chứ không phải "/admin".
// Để trống thì liên kết ở chân trang tự ẩn đi.
AppLinks.AdminUrl = ApiAddress.ResolveLink(builder.Configuration["AdminUrl"], builder.HostEnvironment.BaseAddress);

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(apiBaseUrl),
    // Khách đặt món không chờ nổi lâu hơn thế này; quá hạn thì báo lỗi rõ ràng
    // còn hơn để vòng xoay quay mãi.
    Timeout = TimeSpan.FromSeconds(30)
});

// --- Dịch vụ dùng chung -----------------------------------------------------

// Thông báo nổi — đăng ký trước ApiClient vì ApiClient phụ thuộc vào nó.
builder.Services.AddScoped<AlertService>();

// Cầu nối duy nhất tới backend. Mọi trang gọi qua đây, không dùng HttpClient trực tiếp.
builder.Services.AddScoped<ApiClient>();

// Giỏ hàng — lưu localStorage, tồn tại qua các lần đóng mở trình duyệt.
builder.Services.AddScoped<CartService>();

// Chủ đề sáng/tối — lựa chọn thủ công thắng cấu hình hệ điều hành.
builder.Services.AddScoped<ThemeService>();

var host = builder.Build();

// --- Khôi phục trạng thái đã lưu trước khi vẽ khung hình đầu tiên ------------
// Làm trước RunAsync để người dùng không thấy giỏ hàng rỗng rồi mới nhảy số,
// và không thấy chớp sáng khi họ đã chọn nền tối.
var theme = host.Services.GetRequiredService<ThemeService>();
await theme.InitializeAsync();

var cart = host.Services.GetRequiredService<CartService>();
await cart.LoadAsync();

await host.RunAsync();
