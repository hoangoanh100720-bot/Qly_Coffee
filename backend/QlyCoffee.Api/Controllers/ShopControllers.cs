using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QlyCoffee.Application.Services;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Api.Controllers;

// ==============================================================================
//  CONTROLLER PHÍA CỬA HÀNG (công khai, không cần đăng nhập)
//
//  Controller là lớp MỎNG: xác thực → kiểm tra dữ liệu vào → gọi Application
//  → trả kết quả. Không có logic nghiệp vụ nào ở đây.
//
//  Mọi phản hồi bọc trong ApiResponse<T> để frontend xử lý lỗi thống nhất.
// ==============================================================================

/// <summary>Lớp cơ sở: tiện ích dựng phản hồi và lấy thông tin ngữ cảnh.</summary>
[ApiController]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Chi nhánh hiện tại. MVP chỉ có một chi nhánh nên đọc từ cấu hình.
    /// Khi mở rộng nhiều chi nhánh thì lấy từ claim của token hoặc header.
    /// </summary>
    protected Guid CurrentStoreId
    {
        get
        {
            var fromConfig = Environment.GetEnvironmentVariable("DEFAULT_STORE_ID");
            return Guid.TryParse(fromConfig, out var id) ? id : Guid.Empty;
        }
    }

    /// <summary>Người dùng đang đăng nhập. Null nếu khách vãng lai.</summary>
    protected Guid? CurrentUserId
    {
        get
        {
            var claim = User.FindFirst("sub")?.Value ?? User.FindFirst("userId")?.Value;
            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }

    protected IActionResult Ok<T>(T data) => base.Ok(ApiResponse<T>.Ok(data));

    protected IActionResult Fail<T>(int statusCode, string code, string message, object? details = null)
        => StatusCode(statusCode, ApiResponse<T>.Fail(code, message, details));
}

// ==============================================================================
//  THÔNG TIN CỬA HÀNG
//
//  Giỏ hàng cần biết quán tính thuế GTGT thế nào TRƯỚC khi khách bấm đặt, nếu
//  không thì số thuế chỉ xuất hiện sau khi đơn đã tạo — tức là khách trả tiền
//  xong mới biết mình vừa trả những gì.
// ==============================================================================

[Route("api/shop")]
public class StoreInfoController : BaseApiController
{
    private readonly AppDbContext _db;

    public StoreInfoController(AppDbContext db) => _db = db;

    /// <summary>Thông tin công khai của quán: địa chỉ, giờ mở cửa và cấu hình thuế GTGT.</summary>
    [HttpGet("cua-hang")]
    public async Task<IActionResult> GetStore(CancellationToken ct)
    {
        var store = await _db.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == CurrentStoreId, ct);

        if (store is null)
            return Fail<StoreInfoDto>(404, "NOT_FOUND", "Chưa cấu hình cửa hàng.");

        return Ok(new StoreInfoDto(
            Name:           store.Name,
            Address:        store.Address,
            Phone:          store.Phone,
            Email:          store.Email,
            TaxCode:        store.TaxCode,
            TaxMode:        store.TaxMode,
            VatRatePercent: store.VatRatePercent,
            OpenTime:       store.OpenTime,
            CloseTime:      store.CloseTime,
            IsOpen:         store.IsOpen));
    }
}

// ==============================================================================
//  THỰC ĐƠN
// ==============================================================================

[Route("api/menu")]
public class MenuController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly IAvailabilityService _availability;

    public MenuController(AppDbContext db, IAvailabilityService availability)
    {
        _db = db;
        _availability = availability;
    }

    /// <summary>
    /// Toàn bộ thực đơn: danh mục + món, kèm giá đã áp khuyến mãi và trạng thái tồn kho.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMenu(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Khuyến mãi đang chạy — nạp một lần rồi tra trong bộ nhớ,
        // tránh gọi database lặp cho từng món.
        var activePromos = await _db.Promotions
            .AsNoTracking()
            .Where(p => p.StoreId == CurrentStoreId
                     && p.Status == PromotionStatus.Active
                     && p.StartsAt <= now && p.EndsAt >= now
                     && p.Type == PromotionType.PercentOff)
            .Select(p => new { p.ProductId, p.Value })
            .ToListAsync(ct);

        var promoByProduct = activePromos
            .Where(p => p.ProductId.HasValue)
            .GroupBy(p => p.ProductId!.Value)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Value));

        var globalPromo = activePromos.Where(p => p.ProductId is null)
            .Select(p => p.Value).DefaultIfEmpty(0).Max();

        var products = await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.StoreId == CurrentStoreId && p.IsActive)
            .OrderBy(p => p.Category!.SortOrder).ThenBy(p => p.SortOrder)
            .ToListAsync(ct);

        var cards = products.Select(p =>
        {
            var discount = promoByProduct.TryGetValue(p.Id, out var d)
                ? Math.Max(d, globalPromo)
                : globalPromo;

            var effective = discount > 0
                ? (int)Math.Round(p.BasePrice * (100 - discount) / 100.0)
                : p.BasePrice;

            return new ProductCardDto(
                Id: p.Id,
                Name: p.Name,
                Slug: p.Slug,
                Description: p.Description,
                ImageUrl: p.ImageUrl,
                ColorPrimaryHex: p.ColorPrimaryHex,
                ColorAccentHex: p.ColorAccentHex,
                BasePrice: p.BasePrice,
                EffectivePrice: effective,
                DiscountPercent: discount,
                IsAvailable: p.IsAvailable,
                MaxServings: p.MaxServings,
                UnavailableReason: p.UnavailableReason,
                CategoryName: p.Category?.Name ?? "",
                Tags: string.IsNullOrEmpty(p.Tags)
                    ? Array.Empty<string>()
                    : p.Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }).ToList();

        var categories = await _db.Categories
            .AsNoTracking()
            .Where(c => c.StoreId == CurrentStoreId && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Slug, c.Description, c.ColorHex, c.IconKey,
                c.Products.Count(p => p.IsActive)))
            .ToListAsync(ct);

        return Ok(new { Categories = categories, Products = cards });
    }

    /// <summary>Chi tiết một món: biến thể (size), nhóm topping, tồn kho theo từng size.</summary>
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetProduct(string slug, CancellationToken ct)
    {
        var p = await _db.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Variants.Where(v => v.IsActive))
            .Include(x => x.ModifierLinks).ThenInclude(l => l.Group!).ThenInclude(g => g.Modifiers)
            .FirstOrDefaultAsync(x => x.Slug == slug && x.IsActive, ct);

        if (p is null)
            return Fail<ProductDetailDto>(404, "NOT_FOUND", "Không tìm thấy món này.");

        // Tính tồn kho riêng cho từng size — size L tốn nhiều nguyên liệu hơn
        // nên số ly còn làm được ít hơn.
        var variants = new List<VariantDto>();
        foreach (var v in p.Variants.OrderBy(v => v.SortOrder))
        {
            var (maxServings, _) = await _availability.ComputeMaxServingsAsync(p.Id, v.Id, ct);
            variants.Add(new VariantDto(v.Id, v.Name, v.PriceDelta, v.IsDefault, maxServings));
        }

        var groups = p.ModifierLinks
            .OrderBy(l => l.SortOrder)
            .Where(l => l.Group is not null)
            .Select(l => new ModifierGroupDto(
                l.Group!.Id, l.Group.Name, l.Group.MinSelect, l.Group.MaxSelect, l.Group.IsRequired,
                l.Group.Modifiers.Where(m => m.IsActive).OrderBy(m => m.SortOrder)
                    .Select(m => new ModifierDto(m.Id, m.Name, m.PriceDelta, m.ColorHex, true))
                    .ToList(),
                l.Group.Kind))
            .ToList();

        var now = DateTime.UtcNow;
        var discount = await _db.Promotions
            .Where(x => x.StoreId == CurrentStoreId && x.Status == PromotionStatus.Active
                     && x.StartsAt <= now && x.EndsAt >= now
                     && x.Type == PromotionType.PercentOff
                     && (x.ProductId == p.Id || x.ProductId == null))
            .Select(x => (int?)x.Value)
            .MaxAsync(ct) ?? 0;

        // Gọi bằng THAM SỐ CÓ TÊN, không theo vị trí. DTO này có 18 tham số, trong
        // đó bốn cái liền nhau đều là string? (Description, PairingNote, ImageUrl,
        // UnavailableReason). Thêm một trường vào giữa mà gọi theo vị trí thì trình
        // biên dịch KHÔNG báo gì cả — nó chỉ lặng lẽ đẩy mọi giá trị lệch đi một ô,
        // và lỗi hiện ra thành "mô tả món nằm ở chỗ đường dẫn ảnh".
        return Ok(new ProductDetailDto(
            Id:                p.Id,
            Name:              p.Name,
            Slug:              p.Slug,
            Description:       p.Description,
            PairingNote:       p.PairingNote,
            ImageUrl:          p.ImageUrl,
            ColorPrimaryHex:   p.ColorPrimaryHex,
            ColorAccentHex:    p.ColorAccentHex,
            BasePrice:         p.BasePrice,
            EffectivePrice:    discount > 0
                                   ? (int)Math.Round(p.BasePrice * (100 - discount) / 100.0)
                                   : p.BasePrice,
            DiscountPercent:   discount,
            IsAvailable:       p.IsAvailable,
            MaxServings:       p.MaxServings,
            UnavailableReason: p.UnavailableReason,
            CategoryName:      p.Category?.Name ?? "",
            Tags:              string.IsNullOrEmpty(p.Tags)
                                   ? Array.Empty<string>()
                                   : p.Tags.Split(','),
            Variants:          variants,
            ModifierGroups:    groups));
    }

    /// <summary>
    /// Kiểm tra tồn kho theo thời gian thực cho giỏ hàng.
    /// Frontend gọi khi khách vào trang giỏ, vì tồn kho có thể đã đổi từ lúc thêm món.
    /// </summary>
    [HttpPost("availability")]
    public async Task<IActionResult> CheckAvailability(
        [FromBody] AvailabilityRequest req, CancellationToken ct)
    {
        var result = new Dictionary<Guid, int>();
        foreach (var id in req.ProductIds.Distinct())
        {
            var (max, _) = await _availability.ComputeMaxServingsAsync(id, null, ct);
            result[id] = max;
        }
        return Ok(result);
    }

    public record AvailabilityRequest(List<Guid> ProductIds);
}

// ==============================================================================
//  ĐƠN HÀNG
// ==============================================================================

[Route("api/orders")]
public class OrdersController : BaseApiController
{
    private readonly IOrderService _orders;
    private readonly AppDbContext _db;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrderService orders, AppDbContext db, ILogger<OrdersController> logger)
    {
        _orders = orders;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Tạo đơn. Đơn ở trạng thái Pending, CHƯA trừ kho.
    /// Kho chỉ bị trừ khi nhân viên xác nhận.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest req, CancellationToken ct)
    {
        // ---- Kiểm tra dữ liệu vào ------------------------------------------
        if (string.IsNullOrWhiteSpace(req.CustomerName))
            return Fail<CreateOrderResult>(400, "VALIDATION", "Vui lòng nhập tên người nhận.");

        if (!IsValidVietnamesePhone(req.CustomerPhone))
            return Fail<CreateOrderResult>(400, "VALIDATION",
                "Số điện thoại không hợp lệ. Nhập theo dạng 0901234567.");

        if (req.Items.Count == 0)
            return Fail<CreateOrderResult>(400, "VALIDATION", "Giỏ hàng đang trống.");

        if (req.Items.Any(i => i.Quantity is < 1 or > 20))
            return Fail<CreateOrderResult>(400, "VALIDATION",
                "Số lượng mỗi món phải từ 1 đến 20 ly.");

        try
        {
            var order = await _orders.CreateOrderAsync(req, CurrentStoreId, CurrentUserId, ct);
            return Ok(new CreateOrderResult(
                order.Id, order.Code, order.GrandTotal, order.Status.ToString()));
        }
        catch (StockShortageException ex)
        {
            // Trả 409 kèm danh sách món thiếu để giao diện chỉ đúng chỗ
            return Fail<CreateOrderResult>(409, "INSUFFICIENT_STOCK",
                "Một số món đã hết nguyên liệu.", ex.Shortages);
        }
        catch (BusinessRuleException ex)
        {
            return Fail<CreateOrderResult>(400, "INVALID_ORDER", ex.Message);
        }
    }

    /// <summary>Tra cứu đơn theo mã — không cần đăng nhập, khách dùng để theo dõi.</summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> GetByCode(string code, CancellationToken ct)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Code == code, ct);

        if (order is null)
            return Fail<OrderDto>(404, "NOT_FOUND", $"Không tìm thấy đơn hàng có mã {code}.");

        return Ok(MapOrder(order));
    }

    /// <summary>
    /// Đổi trạng thái đơn. Chuyển sang Confirmed(1) sẽ TRỪ KHO.
    /// Chỉ nhân viên trở lên mới được gọi.
    /// </summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Roles = "Staff,Manager,Owner")]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromBody] UpdateStatusRequest req, CancellationToken ct)
    {
        try
        {
            var order = await _orders.UpdateStatusAsync(
                id, (OrderStatus)req.Status, CurrentUserId ?? Guid.Empty, req.Reason, ct);

            var full = await _db.Orders.AsNoTracking()
                .Include(o => o.Items)
                .FirstAsync(o => o.Id == order.Id, ct);

            return Ok(MapOrder(full));
        }
        catch (InsufficientStockException ex)
        {
            _logger.LogWarning("Xác nhận đơn thất bại do thiếu {Name}", ex.IngredientName);
            return Fail<OrderDto>(409, "INSUFFICIENT_STOCK", ex.Message, new
            {
                ingredientId = ex.IngredientId,
                ingredientName = ex.IngredientName,
                required = ex.Required,
                available = ex.Available
            });
        }
        catch (BusinessRuleException ex)
        {
            return Fail<OrderDto>(400, "INVALID_TRANSITION", ex.Message);
        }
    }

    public record UpdateStatusRequest(int Status, string? Reason);

    // --- Ánh xạ ---------------------------------------------------------------

    private static OrderDto MapOrder(Domain.Entities.Order o) => new(
        o.Id, o.Code, o.CustomerName, o.CustomerPhone,
        (int)o.OrderType, (int)o.Status, OrderService.StatusLabel(o.Status),
        (int)o.PaymentMethod, (int)o.PaymentStatus,
        o.Subtotal, o.DiscountTotal, o.GrandTotal, o.CostTotal,
        o.TaxMode, o.TaxRatePercent, o.NetAmount, o.TaxAmount,
        o.Note, o.PlacedAt, o.ConfirmedAt, o.ReadyAt, o.CompletedAt,
        o.Items.Select(i => new OrderItemDto(
            i.Id, i.ProductId, i.ProductName, i.VariantName,
            i.Product?.ColorPrimaryHex ?? "#4A2C17",
            i.Product?.ColorAccentHex ?? "#C89968",
            i.Quantity, i.UnitPrice, i.LineTotal,
            ParseModifiers(i.ModifiersJson),
            i.Note, i.Product?.ImageUrl)).ToList());

    private static List<CartModifier> ParseModifiers(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<CartModifier>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new();
        }
        catch { return new(); }
    }

    /// <summary>
    /// Kiểm tra số điện thoại Việt Nam: 10 chữ số bắt đầu bằng 0,
    /// hoặc dạng quốc tế +84 theo sau 9 chữ số.
    /// </summary>
    private static bool IsValidVietnamesePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("84")) digits = "0" + digits[2..];
        return digits.Length == 10 && digits[0] == '0' && digits[1] is '3' or '5' or '7' or '8' or '9';
    }
}
