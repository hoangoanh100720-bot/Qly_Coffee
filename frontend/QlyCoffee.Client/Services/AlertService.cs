using Microsoft.JSInterop;

namespace QlyCoffee.Client.Services;

/// <summary>
/// Thông báo nổi (toast) dùng chung toàn ứng dụng.
/// <para>
/// Component <c>AlertHost</c> đăng ký sự kiện <see cref="OnChange"/> và
/// hiển thị danh sách. Mọi thông báo đều gắn <c>aria-live</c> để trình đọc
/// màn hình đọc lên khi có kết quả bất đồng bộ.
/// </para>
/// </summary>
public class AlertService
{
    private readonly List<Alert> _alerts = new();

    public event Action? OnChange;
    public IReadOnlyList<Alert> Alerts => _alerts;

    public void Success(string message, int durationMs = 4000)
        => Push(AlertLevel.Success, message, durationMs);

    public void Error(string message, int durationMs = 8000)
        => Push(AlertLevel.Error, message, durationMs);

    public void Warning(string message, int durationMs = 6000)
        => Push(AlertLevel.Warning, message, durationMs);

    public void Info(string message, int durationMs = 4000)
        => Push(AlertLevel.Info, message, durationMs);

    private void Push(AlertLevel level, string message, int durationMs)
    {
        var alert = new Alert(Guid.NewGuid(), level, message);
        _alerts.Add(alert);
        OnChange?.Invoke();

        // Lỗi tồn lâu hơn thông báo thành công vì người dùng cần thời gian đọc
        _ = Task.Delay(durationMs).ContinueWith(_ => Dismiss(alert.Id));
    }

    public void Dismiss(Guid id)
    {
        _alerts.RemoveAll(a => a.Id == id);
        OnChange?.Invoke();
    }

    public void Clear()
    {
        _alerts.Clear();
        OnChange?.Invoke();
    }
}

public enum AlertLevel { Success, Error, Warning, Info }

public record Alert(Guid Id, AlertLevel Level, string Message);

/// <summary>
/// Đổi giao diện sáng/tối.
/// <para>
/// Ghi thuộc tính <c>data-theme</c> lên thẻ gốc và lưu vào localStorage.
/// CSS được viết sao cho lựa chọn thủ công THẮNG cấu hình hệ điều hành —
/// xem phần tokens trong design-system.css.
/// </para>
/// </summary>
public class ThemeService
{
    private const string StorageKey = "qly-theme";
    private readonly IJSRuntime _js;

    public ThemeService(IJSRuntime js) => _js = js;

    /// <summary>"light" | "dark" | null (theo hệ thống).</summary>
    public string? Current { get; private set; }

    public event Action? OnChange;

    /// <summary>Khôi phục lựa chọn đã lưu. Gọi một lần khi ứng dụng khởi động.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            Current = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(Current))
                await ApplyAsync(Current);
        }
        catch { /* localStorage bị chặn — dùng theo hệ thống */ }
    }

    public async Task ToggleAsync()
    {
        // Chưa chọn gì thì lần bấm đầu tiên chuyển sang ngược lại hệ thống
        var next = Current switch
        {
            "dark" => "light",
            "light" => "dark",
            _ => await IsSystemDarkAsync() ? "light" : "dark"
        };

        await SetAsync(next);
    }

    public async Task SetAsync(string theme)
    {
        Current = theme;
        await ApplyAsync(theme);
        try { await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, theme); }
        catch { }
        OnChange?.Invoke();
    }

    private async Task ApplyAsync(string theme) =>
        await _js.InvokeVoidAsync("qlyTheme.apply", theme);

    private async Task<bool> IsSystemDarkAsync() =>
        await _js.InvokeAsync<bool>("qlyTheme.isSystemDark");
}
