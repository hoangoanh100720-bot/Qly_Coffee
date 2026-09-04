using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QlyCoffee.Application.Services;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Api.Controllers;

// ==============================================================================
//  BÁN HÀNG TẠI QUẦY + MÀN HÌNH PHA CHẾ
//
//  Hai màn hình, hai người dùng khác nhau, nên tách hai controller:
//    · /api/pos    — nhân viên thu ngân bấm đơn cho khách đứng trước mặt
//    · /api/bar    — nhân viên pha nhìn hàng chờ và bấm hoàn tất
//
//  Cả hai cho Staff dùng. Trang quản lý (báo cáo, kho, kế hoạch) mới cần
//  Manager/Owner — thu ngân không có lý do gì để xem giá vốn hay lãi gộp.
// ==============================================================================

// ==============================================================================
//  QUẦY BÁN HÀNG
// ==============================================================================

[Route("api/pos")]
[Authorize(Roles = "Staff,Manager,Owner")]
public class PosController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly IOrderService _orders;
    private readonly IBarQueueService _queue;

    public PosController(AppDbContext db, IOrderService orders, IBarQueueService queue)
    {
        _db = db;
        _orders = orders;
        _queue = queue;
    }

    /// <summary>
    /// Thực đơn dạng lưới cho màn hình bấm đơn.
    /// <para>
    /// Khác <c>/api/menu</c> của trang bán hàng ở hai chỗ: có cả món đang tạm hết
    /// (nhân viên cần thấy để giải thích với khách, thay vì món biến mất không rõ
    /// lý do), và số ly còn làm được đã TRỪ phần đang nằm trong hàng pha.
    /// </para>
    /// </summary>
    [HttpGet("menu")]
    public async Task<IActionResult> GetMenu(CancellationToken ct)
    {
        var categories = await _db.Categories
            .AsNoTracking()
            .Where(c => c.StoreId == CurrentStoreId && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new PosCategoryDto(c.Id, c.Name, c.Slug, c.ColorHex))
            .ToListAsync(ct);

        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.StoreId == CurrentStoreId && p.IsActive)
            .OrderBy(p => p.Category!.SortOrder).ThenBy(p => p.SortOrder)
            .Select(p => new
            {
                p.Id, p.CategoryId, p.Name, p.BasePrice,
                p.ColorPrimaryHex, p.ColorAccentHex, p.ImageUrl,
                p.MaxServings, p.IsAvailable, p.PrepSeconds,
                Variants = p.Variants
                    .Where(v => v.IsActive)
                    .OrderBy(v => v.SortOrder)
                    .Select(v => new PosVariantDto(v.Id, v.Name, v.PriceDelta, v.IsDefault))
                    .ToList()
            })
            .ToListAsync(ct);

        // Trừ phần đã bị các đơn trong hàng pha chiếm chỗ. Không làm bước này thì
        // giờ cao điểm nhân viên vẫn bấm được món mà nguyên liệu thực tế đã hết.
        var queued = await _queue.GetQueuedQuantitiesAsync(CurrentStoreId, ct);

        var result = products.Select(p =>
        {
            var inQueue = queued.TryGetValue(p.Id, out var q) ? q : 0;
            var remaining = p.MaxServings >= 9999
                ? 9999
                : Math.Max(0, p.MaxServings - inQueue);

            return new PosProductDto(
                Id:              p.Id,
                CategoryId:      p.CategoryId,
                Name:            p.Name,
                BasePrice:       p.BasePrice,
                ColorPrimaryHex: p.ColorPrimaryHex,
                ColorAccentHex:  p.ColorAccentHex,
                ImageUrl:        p.ImageUrl,
                MaxServings:     remaining,
                QuantityInQueue: inQueue,
                IsAvailable:     p.IsAvailable && remaining > 0,
                PrepSeconds:     p.PrepSeconds,
                Variants:        p.Variants);
        }).ToList();

        return Ok(new PosMenuDto(categories, result));
    }

    /// <summary>
    /// Ước tính thời gian trước khi bấm đặt, để nhân viên nói ngay với khách.
    /// <para>
    /// Tách riêng khỏi việc tạo đơn vì nhân viên cần thấy con số này ngay khi
    /// giỏ hàng thay đổi — báo "khoảng 6 phút" trước khi khách quyết định gọi
    /// thêm ly nữa thì hữu ích hơn nhiều so với báo sau khi đã chốt đơn.
    /// </para>
    /// </summary>
    [HttpPost("estimate")]
    public async Task<IActionResult> Estimate([FromBody] EstimateRequest req, CancellationToken ct)
    {
        if (req.Items.Count == 0)
            return Ok(new EtaDto(0, 0, 0, 0, 0));

        var eta = await _queue.EstimateAsync(
            CurrentStoreId,
            req.Items.Select(i => (i.ProductId, i.Quantity)).ToList(), ct);

        return Ok(new EtaDto(
            eta.TotalSeconds, eta.Minutes, eta.QueuedItemsAhead,
            eta.WorkAheadSeconds, eta.Stations));
    }

    /// <summary>
    /// Nhận đơn tại quầy. Đơn vào thẳng hàng pha, không qua bước chờ xác nhận.
    /// </summary>
    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest req, CancellationToken ct)
    {
        try
        {
            var order = await _orders.CreateInStoreOrderAsync(
                req, CurrentStoreId, CurrentUserId ?? Guid.Empty, ct);

            var minutes = order.EstimatedReadyAt.HasValue
                ? Math.Max(1, (int)Math.Ceiling((order.EstimatedReadyAt.Value - DateTime.UtcNow).TotalMinutes))
                : 0;

            return Ok(new PosOrderResultDto(
                order.Id, order.Code, order.GrandTotal,
                minutes, order.EstimatedReadyAt,
                order.Items.Sum(i => i.Quantity)));
        }
        catch (StockShortageException ex)
        {
            return Fail<object>(409, "INSUFFICIENT_STOCK",
                "Không đủ nguyên liệu cho đơn này.", ex.Shortages);
        }
        catch (InsufficientStockException ex)
        {
            return Fail<object>(409, "INSUFFICIENT_STOCK", ex.Message, new
            {
                ingredientName = ex.IngredientName,
                required = ex.Required,
                available = ex.Available
            });
        }
        catch (BusinessRuleException ex)
        {
            return Fail<object>(400, "INVALID_ORDER", ex.Message);
        }
    }
}

// ==============================================================================
//  MÀN HÌNH PHA CHẾ
// ==============================================================================

[Route("api/bar")]
[Authorize(Roles = "Staff,Manager,Owner")]
public class BarController : BaseApiController
{
    private readonly IBarQueueService _queue;
    private readonly IOrderService _orders;

    public BarController(IBarQueueService queue, IOrderService orders)
    {
        _queue = queue;
        _orders = orders;
    }

    /// <summary>Hàng pha + thống kê đã bán trong ngày. Màn hình gọi lại mỗi 15 giây.</summary>
    [HttpGet("queue")]
    public async Task<IActionResult> GetQueue(CancellationToken ct)
    {
        var s = await _queue.GetSnapshotAsync(CurrentStoreId, ct);

        return Ok(new BarQueueDto(
            BusinessDate: s.BusinessDate,
            Queue: s.Queue.Select(q => new BarQueueLineDto(
                q.OrderId, q.OrderItemId, q.OrderCode, q.QueuePosition,
                q.CustomerName, q.Channel, q.ProductName, q.VariantName,
                q.ColorPrimaryHex, q.ColorAccentHex, q.ImageUrl,
                q.Quantity, q.Note, q.Modifiers, q.Status,
                q.QueuedAt, q.EstimatedReadyAt, q.WaitedSeconds, q.IsLate)).ToList(),
            SoldToday: s.SoldToday.Select(x => new SoldTodayDto(
                x.ProductId, x.ProductName, x.ColorPrimaryHex, x.ImageUrl,
                x.QuantitySold, x.QuantityInQueue, x.Revenue)).ToList(),
            TotalCupsInQueue: s.TotalCupsInQueue,
            TotalCupsSoldToday: s.TotalCupsSoldToday,
            TotalOrdersToday: s.TotalOrdersToday,
            RevenueToday: s.RevenueToday,
            Stations: s.Stations,
            EstimatedClearSeconds: s.EstimatedClearSeconds));
    }

    /// <summary>Bắt đầu pha một đơn — chuyển sang trạng thái Đang pha.</summary>
    [HttpPost("orders/{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        try
        {
            var order = await _orders.UpdateStatusAsync(
                id, OrderStatus.Preparing, CurrentUserId ?? Guid.Empty, null, ct);
            return Ok(new { order.Id, Status = (int)order.Status });
        }
        catch (BusinessRuleException ex)
        {
            return Fail<object>(400, "INVALID_TRANSITION", ex.Message);
        }
    }

    /// <summary>
    /// Pha xong, giao khách — ⭐ ĐÂY LÀ LÚC TRỪ KHO cho toàn bộ món trong bill.
    /// </summary>
    [HttpPost("orders/{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        try
        {
            var order = await _orders.CompleteOrderAsync(id, CurrentUserId ?? Guid.Empty, ct);

            // Trả về chênh lệch hứa/thực để màn hình báo được "sớm 2 phút" hay
            // "trễ 4 phút". Không đo thì PrepSeconds sẽ sai mãi mà không ai biết.
            var promisedVsActual = order.EstimatedReadyAt.HasValue && order.CompletedAt.HasValue
                ? (int)(order.CompletedAt.Value - order.EstimatedReadyAt.Value).TotalSeconds
                : (int?)null;

            return Ok(new CompleteOrderResultDto(
                order.Id, order.Code, (int)order.Status,
                order.CostTotal, order.GrandTotal - order.CostTotal,
                promisedVsActual));
        }
        catch (InsufficientStockException ex)
        {
            return Fail<object>(409, "INSUFFICIENT_STOCK", ex.Message, new
            {
                ingredientName = ex.IngredientName,
                required = ex.Required,
                available = ex.Available
            });
        }
        catch (BusinessRuleException ex)
        {
            return Fail<object>(400, "INVALID_TRANSITION", ex.Message);
        }
    }

    /// <summary>Hủy đơn đang trong hàng pha. Bắt buộc có lý do.</summary>
    [HttpPost("orders/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid id, [FromBody] CancelOrderRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return Fail<object>(400, "VALIDATION", "Phải nhập lý do hủy đơn.");

        try
        {
            var order = await _orders.CancelOrderAsync(
                id, CurrentUserId ?? Guid.Empty, req.Reason.Trim(), ct);
            return Ok(new { order.Id, Status = (int)order.Status, order.StockReturned });
        }
        catch (BusinessRuleException ex)
        {
            return Fail<object>(400, "INVALID_TRANSITION", ex.Message);
        }
    }

    public record CancelOrderRequest(string Reason);
}
