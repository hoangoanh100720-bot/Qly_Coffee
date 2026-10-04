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

    /// <summary>
    /// Đọc token từ localStorage và gắn vào <see cref="HttpClient"/>. Gọi bao
    /// nhiêu lần cũng được, trả về <c>true</c> nếu có token còn hạn.
    /// <para>
    /// VÌ SAO PHẢI CÓ HÀM NÀY RIÊNG
    /// Trước đây token chỉ được gắn như TÁC DỤNG PHỤ của
    /// <see cref="GetAuthenticationStateAsync"/>. Thành phần nào gọi API trong
    /// <c>OnInitializedAsync</c> của mình mà chạy trước hàm đó thì gửi đi một
    /// yêu cầu KHÔNG có token và nhận về 401 — đúng chuyện đã xảy ra với các
    /// con số huy hiệu trên thanh điều hướng: mỗi lần tải lại trang quản lý là
    /// một lần 401 trong console, và bốn con số đó im lặng không bao giờ hiện.
    ///
    /// Lỗi kiểu này không bao giờ lộ ra khi bấm chuyển trang trong app, chỉ lộ
    /// khi tải lại trang bằng F5 — nên rất dễ sống sót qua mọi lần thử tay.
    ///
    /// Giờ việc gắn token là một hành động có tên, gọi được một cách tường
    /// minh, và ai cần token trước khi gọi API thì chờ nó.
    /// </para>
    /// </summary>
    public async Task<bool> EnsureAuthHeaderAsync()
    {
        try
        {
            var token = await _js.InvokeAsync<string?>("localStorage.getItem", TokenKey);

            if (string.IsNullOrWhiteSpace(token) || IsTokenExpired(token))
            {
                _http.DefaultRequestHeaders.Authorization = null;
                return false;
            }

            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return true;
        }
        catch
        {
            // localStorage bị chặn (cửa sổ ẩn danh, trình duyệt khoá site data)
            return false;
        }
    }

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

            // Gắn token vào mọi lời gọi API sau này. Dùng chung đúng một đường
            // đi với EnsureAuthHeaderAsync để hai nơi không bao giờ lệch nhau.
            await EnsureAuthHeaderAsync();

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
