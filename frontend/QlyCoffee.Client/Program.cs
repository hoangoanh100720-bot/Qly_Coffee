using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using QlyCoffee.Client;
using QlyCoffee.Client.Services;

// ==============================================================================
//  ĐIỂM KHỞI ĐỘNG CỦA FRONTEND (Blazor WebAssembly)
//
//  Địa chỉ backend KHÔNG viết cứng ở đây. Nó nằm trong wwwroot/appsettings.json,
//  file này được sinh ra từ biến CLIENT_API_URL trong .env lúc build
//  (xem script scripts/gen-client-config.ps1). Nhờ vậy đổi máy chủ chỉ cần sửa
//  .env, không phải build lại mã nguồn.
// ==============================================================================

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// --- Địa chỉ backend --------------------------------------------------------
var apiBaseUrl = builder.Configuration["ApiBaseUrl"];

if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    // Không có cấu hình thì mặc định gọi cùng máy chủ đang phục vụ frontend.
    // Trường hợp này xảy ra khi backend đứng luôn làm host cho file tĩnh.
    apiBaseUrl = builder.HostEnvironment.BaseAddress;
}

// Ảnh món lưu ở backend nên cần biết địa chỉ backend để ghép đường dẫn
QlyCoffee.Client.Services.Media.ApiBaseUrl = apiBaseUrl;

// --- Tài khoản nhận chuyển khoản --------------------------------------------
// Dùng cho mã QR ở màn hình bán hàng tại quầy. Cũng đi ra từ .env qua
// scripts/gen-client-config.ps1, cùng đường với ApiBaseUrl ở trên.
// Thiếu cấu hình không phải là lỗi: BankQr.IsConfigured sẽ false và màn hình
// quầy chỉ nhắc nhân viên khai báo, chứ không hỏng.
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

// Giỏ hàng — lưu localStorage, tồn tại qua các lần đóng mở trình duyệt.
builder.Services.AddScoped<CartService>();

// Chủ đề sáng/tối — lựa chọn thủ công thắng cấu hình hệ điều hành.
builder.Services.AddScoped<ThemeService>();

// --- Xác thực ---------------------------------------------------------------
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthStateProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(
    sp => sp.GetRequiredService<AuthStateProvider>());

var host = builder.Build();

// --- Khôi phục trạng thái đã lưu trước khi vẽ khung hình đầu tiên ------------
// Làm trước RunAsync để người dùng không thấy giỏ hàng rỗng rồi mới nhảy số,
// và không thấy chớp sáng khi họ đã chọn nền tối.
var theme = host.Services.GetRequiredService<ThemeService>();
await theme.InitializeAsync();

var cart = host.Services.GetRequiredService<CartService>();
await cart.LoadAsync();

await host.RunAsync();
