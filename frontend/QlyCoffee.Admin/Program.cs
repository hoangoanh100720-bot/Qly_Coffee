using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using QlyCoffee.Admin;
using QlyCoffee.Client.Services;

// ==============================================================================
//  ĐIỂM KHỞI ĐỘNG CỦA APP QUẢN LÝ (Blazor WebAssembly, chạy như PWA)
//
//  Địa chỉ backend và các tham số khác nằm trong wwwroot/appsettings.json, sinh
//  ra từ .env bằng scripts/gen-client-config.ps1. Không viết cứng ở đây.
//
//  ⚠️ appsettings.json của app này AI TẢI VỀ CŨNG ĐỌC ĐƯỢC. Chỉ để trong đó
//  những gì công khai được: địa chỉ API, số tài khoản nhận tiền (vốn đã in trên
//  mã QR cho khách quét). Khóa API, mật khẩu, JWT_SECRET thì tuyệt đối không.
// ==============================================================================

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// --- Địa chỉ backend --------------------------------------------------------
// Xem ghi chú trong QlyCoffee.Client/Program.cs và QlyCoffee.Ui/Services/ApiAddress.cs:
// địa chỉ IP ghi cứng trong appsettings.json chết mỗi lần đổi wifi, nên host
// được suy từ chính trang đang mở khi cả hai đều nằm trong mạng nội bộ.
var apiBaseUrl = ApiAddress.Resolve(
    builder.Configuration["ApiBaseUrl"],
    builder.HostEnvironment.BaseAddress);

// Ảnh món lưu ở backend nên cần biết địa chỉ backend để ghép đường dẫn
Media.ApiBaseUrl = apiBaseUrl;

// --- Liên kết sang trang bán hàng -------------------------------------------
// Hai ứng dụng có thể ở hai tên miền, nên đây là cấu hình chứ không phải "/".
AppLinks.ShopUrl = ApiAddress.ResolveLink(builder.Configuration["ShopUrl"], builder.HostEnvironment.BaseAddress);

// --- Tài khoản nhận chuyển khoản --------------------------------------------
// Dùng cho mã QR ở màn hình bán hàng tại quầy. Thiếu cấu hình không phải là lỗi:
// BankQr.IsConfigured sẽ false và màn hình quầy chỉ nhắc nhân viên khai báo.
BankQr.BankCode      = builder.Configuration["Payment:BankCode"]      ?? "";
BankQr.AccountNumber = builder.Configuration["Payment:AccountNumber"] ?? "";
BankQr.AccountName   = builder.Configuration["Payment:AccountName"]   ?? "";

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(apiBaseUrl),
    // Đủ dài cho job phân tích kế hoạch (có gọi AI), nhưng không để treo vô hạn.
    Timeout = TimeSpan.FromSeconds(60)
});

// --- Dịch vụ dùng chung -----------------------------------------------------

// Thông báo nổi — đăng ký trước ApiClient vì ApiClient phụ thuộc vào nó.
builder.Services.AddScoped<AlertService>();

// Cầu nối duy nhất tới backend. Mọi trang gọi qua đây, không dùng HttpClient trực tiếp.
builder.Services.AddScoped<ApiClient>();

// Chủ đề sáng/tối — lựa chọn thủ công thắng cấu hình hệ điều hành.
builder.Services.AddScoped<ThemeService>();

// --- Xác thực ---------------------------------------------------------------
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    sp => sp.GetRequiredService<AuthStateProvider>());

var host = builder.Build();

// --- Khôi phục chủ đề trước khi vẽ khung hình đầu tiên ----------------------
// Làm trước RunAsync để người đã chọn nền tối không thấy một chớp trắng.
var theme = host.Services.GetRequiredService<ThemeService>();
await theme.InitializeAsync();

await host.RunAsync();
