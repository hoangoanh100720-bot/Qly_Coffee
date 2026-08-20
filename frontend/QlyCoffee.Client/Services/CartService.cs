using System.Text.Json;
using Microsoft.JSInterop;
using QlyCoffee.Shared;

namespace QlyCoffee.Client.Services;

/// <summary>
/// Giỏ hàng phía client.
/// <para>
/// Lưu vào localStorage để khách đóng trình duyệt rồi mở lại vẫn còn giỏ.
/// Giá được tính lại ở BACKEND khi đặt đơn — giá lưu ở đây chỉ để hiển thị,
/// không bao giờ tin tưởng số tiền do client gửi lên.
/// </para>
/// </summary>
public class CartService
{
    private const string StorageKey = "qly-cart-v1";

    private readonly IJSRuntime _js;
    private List<CartItem> _items = new();
    private bool _loaded;

    public CartService(IJSRuntime js) => _js = js;

    /// <summary>Bắn ra mỗi khi giỏ đổi, để số trên nút giỏ và các trang cập nhật ngay.</summary>
    public event Action? OnChange;

    public IReadOnlyList<CartItem> Items => _items;

    /// <summary>Tổng số ly trong giỏ — hiển thị trên huy hiệu nút giỏ hàng.</summary>
    public int TotalQuantity => _items.Sum(i => i.Quantity);

    /// <summary>Tổng tiền tạm tính (đồng).</summary>
    public int Subtotal => _items.Sum(i => i.LineTotal);

    /// <summary>Tổng tiền đã tiết kiệm nhờ khuyến mãi (đồng).</summary>
    public int TotalSaved =>
        _items.Sum(i => (i.OriginalUnitPrice - i.UnitPrice) * i.Quantity);

    // --- Nạp & lưu ------------------------------------------------------------

    /// <summary>Nạp giỏ từ localStorage. Gọi một lần khi ứng dụng khởi động.</summary>
    public async Task LoadAsync()
    {
        if (_loaded) return;
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrWhiteSpace(json))
                _items = JsonSerializer.Deserialize<List<CartItem>>(json) ?? new();
        }
        catch
        {
            // localStorage có thể bị chặn ở chế độ riêng tư — bỏ qua, dùng giỏ rỗng
            _items = new();
        }
        _loaded = true;
        OnChange?.Invoke();
    }

    private async Task PersistAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_items);
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
        catch { /* không lưu được thì giỏ vẫn hoạt động trong phiên hiện tại */ }

        OnChange?.Invoke();
    }

    // --- Thao tác -------------------------------------------------------------

    /// <summary>
    /// Thêm món vào giỏ. Nếu đã có dòng cùng cấu hình (cùng món, cùng size,
    /// cùng bộ topping) thì cộng dồn số lượng thay vì tạo dòng mới.
    /// </summary>
    public async Task AddAsync(CartItem item)
    {
        item.LineKey = BuildLineKey(item);

        var existing = _items.FirstOrDefault(i => i.LineKey == item.LineKey);
        if (existing is not null)
        {
            // Không cho vượt quá số ly tồn kho còn làm được
            existing.Quantity = Math.Min(
                existing.Quantity + item.Quantity,
                existing.MaxServings);
        }
        else
        {
            item.Quantity = Math.Min(item.Quantity, item.MaxServings);
            _items.Add(item);
        }

        await PersistAsync();
    }

    /// <summary>Đổi số lượng của một dòng. Đặt về 0 hoặc âm thì xóa dòng.</summary>
    public async Task SetQuantityAsync(string lineKey, int quantity)
    {
        var item = _items.FirstOrDefault(i => i.LineKey == lineKey);
        if (item is null) return;

        if (quantity <= 0)
            _items.Remove(item);
        else
            item.Quantity = Math.Min(quantity, item.MaxServings);

        await PersistAsync();
    }

    public async Task RemoveAsync(string lineKey)
    {
        _items.RemoveAll(i => i.LineKey == lineKey);
        await PersistAsync();
    }

    public async Task ClearAsync()
    {
        _items.Clear();
        await PersistAsync();
    }

    /// <summary>
    /// Đồng bộ trần số lượng theo tồn kho mới nhất.
    /// Gọi khi vào trang giỏ hàng — tồn kho có thể đã đổi từ lúc khách thêm món.
    /// </summary>
    public async Task SyncAvailabilityAsync(IReadOnlyDictionary<Guid, int> maxServingsByProduct)
    {
        var changed = false;

        foreach (var item in _items.ToList())
        {
            if (!maxServingsByProduct.TryGetValue(item.ProductId, out var max))
                continue;

            item.MaxServings = max;

            if (max == 0)
            {
                _items.Remove(item);
                changed = true;
            }
            else if (item.Quantity > max)
            {
                item.Quantity = max;
                changed = true;
            }
        }

        if (changed) await PersistAsync();
    }

    /// <summary>Chuyển giỏ thành dữ liệu gửi lên API khi đặt đơn.</summary>
    public List<CreateOrderItem> ToOrderItems() =>
        _items.Select(i => new CreateOrderItem
        {
            ProductId = i.ProductId,
            VariantId = i.VariantId,
            ModifierIds = i.Modifiers.Select(m => m.ModifierId).ToList(),
            Quantity = i.Quantity,
            Note = i.Note
        }).ToList();

    /// <summary>
    /// Khóa nhận dạng dòng giỏ hàng. Sắp xếp id topping trước khi ghép để
    /// "trân châu + thạch" và "thạch + trân châu" được coi là cùng một dòng.
    /// </summary>
    private static string BuildLineKey(CartItem item)
    {
        var mods = string.Join(",", item.Modifiers.Select(m => m.ModifierId).OrderBy(x => x));
        return $"{item.ProductId}|{item.VariantId}|{mods}|{item.Note}";
    }
}
