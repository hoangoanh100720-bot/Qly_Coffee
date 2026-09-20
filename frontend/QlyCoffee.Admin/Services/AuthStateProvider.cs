using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using QlyCoffee.Shared;

namespace QlyCoffee.Client.Services;

// ==============================================================================
//  QUẢN LÝ TRẠNG THÁI ĐĂNG NHẬP
//
//  Blazor WebAssembly chạy hoàn toàn trên trình duyệt nên không có session
//  phía máy chủ. Token lưu trong localStorage và được đọc lại mỗi lần mở app.
//
//  ⚠️ LƯU Ý BẢO MẬT
//  Token trong localStorage có thể bị đọc nếu trang dính XSS. Đây là đánh đổi
//  đã biết của mọi ứng dụng SPA. Ba biện pháp giảm thiểu:
//    1. Token sống ngắn (8 tiếng, cấu hình ở JWT_EXPIRY_MINUTES)
//    2. Không bao giờ dùng @((MarkupString)) với dữ liệu người dùng nhập
//    3. Quyền được kiểm ở BACKEND, giao diện chỉ để trải nghiệm tốt hơn —
//       kẻ tấn công sửa token phía client vẫn không gọi được API
// ==============================================================================

public class AuthStateProvider : AuthenticationStateProvider
{
    private const string TokenKey = "qly-token";
    private const string UserKey = "qly-user";

    private readonly IJSRuntime _js;
    private readonly HttpClient _http;

    private ClaimsPrincipal _anonymous = new(new ClaimsIdentity());
    private UserDto? _currentUser;

    public AuthStateProvider(IJSRuntime js, HttpClient http)
    {
        _js = js;
        _http = http;
    }

    /// <summary>Người dùng đang đăng nhập, null nếu chưa.</summary>
    public UserDto? CurrentUser => _currentUser;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var token = await _js.InvokeAsync<string?>("localStorage.getItem", TokenKey);
            if (string.IsNullOrWhiteSpace(token))
                return new AuthenticationState(_anonymous);

            // Hết hạn thì dọn sạch, không để trạng thái nửa vời
            if (IsTokenExpired(token))
            {
                await LogoutAsync();
                return new AuthenticationState(_anonymous);
            }

            var userJson = await _js.InvokeAsync<string?>("localStorage.getItem", UserKey);
            if (!string.IsNullOrWhiteSpace(userJson))
                _currentUser = JsonSerializer.Deserialize<UserDto>(userJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            // Gắn token vào mọi lời gọi API sau này
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var identity = new ClaimsIdentity(ParseClaims(token), "jwt");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            // localStorage bị chặn hoặc token hỏng — coi như chưa đăng nhập
            return new AuthenticationState(_anonymous);
        }
    }

    /// <summary>Lưu token sau khi đăng nhập thành công và thông báo cho toàn ứng dụng.</summary>
    public async Task LoginAsync(LoginResult result)
    {
        await _js.InvokeVoidAsync("localStorage.setItem", TokenKey, result.AccessToken);
        await _js.InvokeVoidAsync("localStorage.setItem", UserKey,
            JsonSerializer.Serialize(result.User));

        _currentUser = result.User;
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.AccessToken);

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task LogoutAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", TokenKey);
            await _js.InvokeVoidAsync("localStorage.removeItem", UserKey);
        }
        catch { }

        _currentUser = null;
        _http.DefaultRequestHeaders.Authorization = null;

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_anonymous)));
    }

    // ==========================================================================
    //  ĐỌC JWT
    // ==========================================================================

    /// <summary>
    /// Giải mã phần payload của JWT để lấy claims.
    /// <para>
    /// KHÔNG kiểm tra chữ ký ở đây — việc đó là của backend. Phía client chỉ
    /// cần biết tên và vai trò để hiển thị đúng giao diện.
    /// </para>
    /// </summary>
    private static IEnumerable<Claim> ParseClaims(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return Array.Empty<Claim>();

        var payload = DecodeBase64Url(parts[1]);
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payload);
        if (dict is null) return Array.Empty<Claim>();

        var claims = new List<Claim>();
        foreach (var (key, value) in dict)
        {
            // Tên claim chuẩn .NET dài dòng, ánh xạ về dạng ngắn cho dễ dùng
            var claimType = key switch
            {
                "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name" => ClaimTypes.Name,
                "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" => ClaimTypes.Role,
                "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress" => ClaimTypes.Email,
                _ => key
            };

            if (value.ValueKind == JsonValueKind.Array)
                claims.AddRange(value.EnumerateArray()
                    .Select(v => new Claim(claimType, v.ToString())));
            else
                claims.Add(new Claim(claimType, value.ToString()));
        }

        return claims;
    }

    private static bool IsTokenExpired(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return true;

            var payload = DecodeBase64Url(parts[1]);
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payload);

            if (dict is null || !dict.TryGetValue("exp", out var exp)) return true;

            var expiry = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
            // Trừ hao 30 giây để không dùng token sắp hết hạn giữa chừng
            return expiry <= DateTimeOffset.UtcNow.AddSeconds(30);
        }
        catch { return true; }
    }

    /// <summary>
    /// Giải mã base64url. JWT dùng biến thể base64 thay '+' bằng '-', '/' bằng '_'
    /// và bỏ dấu '=' đệm ở cuối — phải bù lại trước khi giải mã.
    /// </summary>
    private static string DecodeBase64Url(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
