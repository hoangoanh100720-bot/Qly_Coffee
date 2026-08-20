using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QlyCoffee.Application.Services;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Api.Controllers;

// ==============================================================================
//  KHUYẾN MÃI & BÁO CÁO
// ==============================================================================

[Route("api/admin/promotions")]
[Authorize(Roles = "Manager,Owner")]
public class PromotionsController : BaseApiController
{
    private readonly AppDbContext _db;

    public PromotionsController(AppDbContext db) => _db = db;

    /// <summary>
    /// Danh sách khuyến mãi kèm số liệu hiệu quả.
    /// <para>
    /// Cột quan trọng nhất là <c>SourcePlanId</c>: nó cho biết khuyến mãi này do
    /// AI đề xuất hay do người tự tạo. Tỷ lệ khuyến mãi từ AI được duyệt và chạy
    /// hiệu quả chính là thước đo chất lượng của cả tính năng kế hoạch hằng ngày.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var promotions = await _db.Promotions
            .AsNoTracking()
            .Include(p => p.Product)
            .Where(p => p.StoreId == CurrentStoreId)
            .OrderByDescending(p => p.StartsAt)
            .Take(100)
            .ToListAsync(ct);

        var result = new List<PromotionDto>();

        foreach (var p in promotions)
        {
            // Đếm số đơn thực sự đã áp dụng khuyến mãi này, và doanh thu sinh ra.
            // Không dựa vào RedemptionCount vì cột đó có thể lệch nếu có lỗi giữa chừng.
            var stats = p.ProductId is Guid pid
                ? await _db.OrderItems
                    .AsNoTracking()
                    .Where(i => i.ProductId == pid
                             && i.Order!.StoreId == CurrentStoreId
                             && i.Order.PlacedAt >= p.StartsAt
                             && i.Order.PlacedAt <= (p.EndsAt < now ? p.EndsAt : now)
                             && i.Order.Status != OrderStatus.Cancelled
                             && i.Order.Status != OrderStatus.Pending)
                    .GroupBy(i => 1)
                    .Select(g => new { Units = g.Sum(x => x.Quantity), Revenue = g.Sum(x => x.LineTotal) })
                    .FirstOrDefaultAsync(ct)
                : null;

            result.Add(new PromotionDto(
                Id: p.Id,
                Name: p.Name,
                Description: p.Description,
                Type: (int)p.Type,
                Value: p.Value,
                Status: (int)p.Status,
                StatusLabel: StatusLabel(p.Status, p.EndsAt, now),
                StartsAt: p.StartsAt,
                EndsAt: p.EndsAt,
                ProductId: p.ProductId,
                ProductName: p.Product?.Name,
                ProductColorHex: p.Product?.ColorPrimaryHex,
                BannerText: p.BannerText,
                IsFromAi: p.SourcePlanId != null,
                UnitsSold: stats?.Units ?? 0,
                Revenue: stats?.Revenue ?? 0,
                IsRunning: p.Status == PromotionStatus.Active && p.StartsAt <= now && p.EndsAt >= now));
        }

        return Ok(result);
    }

    /// <summary>Tạm dừng hoặc chạy lại một chương trình khuyến mãi.</summary>
    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid id, CancellationToken ct)
    {
        var promo = await _db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (promo is null) return Fail<bool>(404, "NOT_FOUND", "Không tìm thấy chương trình khuyến mãi.");

        promo.Status = promo.Status == PromotionStatus.Active
            ? PromotionStatus.Paused
            : PromotionStatus.Active;

        await _db.SaveChangesAsync(ct);
        return Ok(true);
    }

    /// <summary>Kết thúc sớm một chương trình khuyến mãi.</summary>
    [HttpPost("{id:guid}/stop")]
    public async Task<IActionResult> Stop(Guid id, CancellationToken ct)
    {
        var promo = await _db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (promo is null) return Fail<bool>(404, "NOT_FOUND", "Không tìm thấy chương trình khuyến mãi.");

        promo.Status = PromotionStatus.Expired;
        promo.EndsAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(true);
    }

    private static string StatusLabel(PromotionStatus status, DateTime endsAt, DateTime now)
        => status switch
        {
            PromotionStatus.Active when endsAt < now => "Đã hết hạn",
            PromotionStatus.Active  => "Đang chạy",
            PromotionStatus.Paused  => "Tạm dừng",
            PromotionStatus.Expired => "Đã kết thúc",
            _                       => "Bản nháp"
        };
}

// ==============================================================================
//  BÁO CÁO
// ==============================================================================

[Route("api/admin/reports")]
[Authorize(Roles = "Manager,Owner")]
public class ReportsController : BaseApiController
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db) => _db = db;

    /// <summary>
    /// Báo cáo tổng hợp theo khoảng ngày.
    /// <para>
    /// Đọc từ hai bảng tổng hợp <c>daily_sales</c> và <c>daily_consumptions</c>
    /// thay vì quét sổ cái — nhanh hơn hàng chục lần khi dữ liệu lớn dần.
    /// Hai bảng này được job cuối ngày ghi một lần.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var today = VietnamTime.Now().Date;
        var fromDate = DateTime.TryParse(from, out var f) ? f.Date : today.AddDays(-29);
        var toDate = DateTime.TryParse(to, out var t) ? t.Date : today;

        var fromKey = fromDate.ToString("yyyy-MM-dd");
        var toKey = toDate.ToString("yyyy-MM-dd");

        // ---- Doanh thu theo ngày -------------------------------------------
        var salesRows = await _db.DailySales
            .AsNoTracking()
            .Where(s => s.StoreId == CurrentStoreId
                     && string.Compare(s.BusinessDate, fromKey) >= 0
                     && string.Compare(s.BusinessDate, toKey) <= 0)
            .ToListAsync(ct);

        var trend = salesRows
            .GroupBy(s => s.BusinessDate)
            .Select(g => new DailyRevenuePoint(
                g.Key,
                g.Sum(x => x.Revenue),
                0))
            .OrderBy(p => p.BusinessDate)
            .ToList();

        // ---- Đơn hàng thực tế trong khoảng ---------------------------------
        var fromUtc = VietnamTime.ToUtc(fromDate);
        var toUtc = VietnamTime.ToUtc(toDate.AddDays(1));

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.StoreId == CurrentStoreId
                     && o.PlacedAt >= fromUtc && o.PlacedAt < toUtc
                     && o.Status != OrderStatus.Cancelled
                     && o.Status != OrderStatus.Pending)
            .Select(o => new { o.GrandTotal, o.CostTotal })
            .ToListAsync(ct);

        var revenue = orders.Sum(o => o.GrandTotal);
        var cost = orders.Sum(o => o.CostTotal);

        // ---- Hao hụt --------------------------------------------------------
        var wasteMovements = await _db.StockMovements
            .AsNoTracking()
            .Include(m => m.Ingredient)
            .Where(m => m.StoreId == CurrentStoreId
                     && m.OccurredAt >= fromUtc && m.OccurredAt < toUtc
                     && (m.Type == MovementType.Waste || m.Type == MovementType.ExpiredOut))
            .ToListAsync(ct);

        var wasteTotal = wasteMovements.Sum(m => m.TotalCost);

        var wasteByIngredient = wasteMovements
            .Where(m => m.Ingredient != null)
            .GroupBy(m => new { m.IngredientId, m.Ingredient!.Name, m.Ingredient.ColorHex, m.Ingredient.IconKey })
            .Select(g => new WasteBreakdownDto(
                g.Key.IngredientId,
                g.Key.Name,
                g.Key.ColorHex,
                g.Key.IconKey,
                g.Sum(m => Math.Abs(m.QuantityDelta)),
                g.Sum(m => m.TotalCost),
                // Tách riêng phần hết hạn — đây là loại hao hụt mà tính năng
                // kế hoạch AI sinh ra để giảm bớt
                g.Where(m => m.Type == MovementType.ExpiredOut).Sum(m => m.TotalCost)))
            .OrderByDescending(w => w.TotalCost)
            .Take(15)
            .ToList();

        // ---- Món bán chạy ----------------------------------------------------
        var productIds = salesRows.Select(s => s.ProductId).Distinct().ToList();

        var products = await _db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.Name, p.ColorPrimaryHex }, ct);

        var topProducts = salesRows
            .GroupBy(s => s.ProductId)
            .Select(g => new TopProductDto(
                g.Key,
                products.TryGetValue(g.Key, out var p) ? p.Name : "Món đã xóa",
                products.TryGetValue(g.Key, out var p2) ? p2.ColorPrimaryHex : "#4A2C17",
                g.Sum(x => x.UnitsSold),
                g.Sum(x => x.Revenue)))
            .OrderByDescending(x => x.Revenue)
            .Take(15)
            .ToList();

        return Ok(new ReportDto(
            FromDate: fromKey,
            ToDate: toKey,
            Revenue: revenue,
            Cost: cost,
            GrossProfit: revenue - cost,
            OrderCount: orders.Count,
            AverageOrderValue: orders.Count > 0 ? revenue / orders.Count : 0,
            WasteValue: wasteTotal,
            // Tỷ lệ hao hụt trên doanh thu — chỉ số ngành, dưới 3% là tốt
            WastePercent: revenue > 0 ? Math.Round(wasteTotal * 100.0 / revenue, 2) : 0,
            RevenueTrend: trend,
            TopProducts: topProducts,
            WasteBreakdown: wasteByIngredient));
    }
}
