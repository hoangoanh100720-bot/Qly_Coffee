using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;

namespace QlyCoffee.Application.Services;

// ==============================================================================
//  HÀNG PHA CHẾ & THỜI GIAN BÁO KHÁCH
// ==============================================================================
//  Hai việc, nhưng gắn chặt nhau nên để chung một lớp:
//
//    1. Xếp hàng những món đang chờ pha, đúng thứ tự đơn vào
//    2. Tính xem đơn mới sẽ xong lúc nào, dựa trên hàng chờ đang dài bao nhiêu
//
//  TẠI SAO TỰ TÍNH CHỨ KHÔNG HỎI AI:
//  Thời gian chờ là phép cộng thuần túy — tổng thời gian pha chia cho số nhân
//  viên đứng quầy. Một câu trả lời sai ở đây làm khách chờ hụt và nhân viên mất
//  uy tín, mà mô hình ngôn ngữ thì không có cách nào biết quầy đang có mấy người.
//  Giữ nguyên nguyên tắc ba lớp của hệ thống: số do code tính, AI chỉ viết chữ.
// ==============================================================================

public interface IBarQueueService
{
    /// <summary>Toàn cảnh hàng pha: đơn đang chờ, món đang làm, đã bán hôm nay.</summary>
    Task<BarQueueSnapshot> GetSnapshotAsync(Guid storeId, CancellationToken ct = default);

    /// <summary>Ước tính lúc nào món xong, cho một đơn SẮP được nhận.</summary>
    Task<EtaEstimate> EstimateAsync(
        Guid storeId, IReadOnlyList<(Guid ProductId, int Quantity)> items, CancellationToken ct = default);

    /// <summary>
    /// Tổng số ly của từng món ĐANG NẰM TRONG HÀNG PHA (chưa trừ kho).
    /// Dùng để trừ bớt khi tính số ly còn làm được — nếu không, giờ cao điểm sẽ
    /// nhận đơn vượt quá nguyên liệu thật sự còn lại.
    /// </summary>
    Task<Dictionary<Guid, int>> GetQueuedQuantitiesAsync(Guid storeId, CancellationToken ct = default);
}

/// <summary>Tham số quầy pha, đọc từ .env.</summary>
public class BarOptions
{
    /// <summary>
    /// Số nhân viên pha đồng thời. Hai người thì hàng chờ vơi nhanh gấp đôi.
    /// Đặt sai con số này là thời gian báo khách sai theo đúng tỉ lệ đó.
    /// </summary>
    public int Stations { get; set; } = 2;

    /// <summary>
    /// Giây cộng thêm cho mỗi đơn: lấy ly, đóng nắp, dán tem, gọi tên khách.
    /// Không phụ thuộc số ly nên cộng một lần cho cả đơn.
    /// </summary>
    public int HandoffSeconds { get; set; } = 45;

    /// <summary>
    /// Ly thứ hai trở đi của CÙNG một món chỉ tốn bằng chừng này phần thời gian.
    /// Pha 3 ly cà phê sữa đá một lượt nhanh hơn hẳn pha 3 món khác nhau vì
    /// nhân viên chỉ lấy nguyên liệu một lần.
    /// </summary>
    public double BatchFactor { get; set; } = 0.55;

    /// <summary>
    /// Làm tròn thời gian báo khách lên bội số của chừng này giây.
    /// Nói "khoảng 5 phút" nghe tin được; nói "4 phút 12 giây" nghe như máy đọc.
    /// </summary>
    public int RoundToSeconds { get; set; } = 60;
}

/// <summary>Thời gian ước tính, kèm số liệu đã dùng để tính ra nó.</summary>
public record EtaEstimate(
    int TotalSeconds,
    int Minutes,
    DateTime ReadyAtUtc,
    int QueuedItemsAhead,
    int WorkAheadSeconds,
    int OwnWorkSeconds,
    int Stations);

/// <summary>Một dòng món trong hàng pha.</summary>
public record BarQueueLine(
    Guid OrderId,
    Guid OrderItemId,
    string OrderCode,
    int QueuePosition,
    string CustomerName,
    int Channel,
    string ProductName,
    string? VariantName,
    string ColorPrimaryHex,
    string ColorAccentHex,
    string? ImageUrl,
    int Quantity,
    string? Note,
    string Modifiers,
    int Status,
    DateTime QueuedAt,
    DateTime? EstimatedReadyAt,
    int WaitedSeconds,
    bool IsLate);

/// <summary>Tổng số ly đã bán của một món trong ngày.</summary>
public record SoldTodayLine(
    Guid ProductId,
    string ProductName,
    string ColorPrimaryHex,
    string? ImageUrl,
    int QuantitySold,
    int QuantityInQueue,
    int Revenue);

/// <summary>Toàn cảnh màn hình pha chế.</summary>
public record BarQueueSnapshot(
    string BusinessDate,
    IReadOnlyList<BarQueueLine> Queue,
    IReadOnlyList<SoldTodayLine> SoldToday,
    int TotalCupsInQueue,
    int TotalCupsSoldToday,
    int TotalOrdersToday,
    int RevenueToday,
    int Stations,
    int EstimatedClearSeconds);

public class BarQueueService : IBarQueueService
{
    private readonly AppDbContext _db;
    private readonly BarOptions _opt;
    private readonly ILogger<BarQueueService> _logger;

    /// <summary>Những trạng thái được coi là "đang trong hàng pha".</summary>
    private static readonly OrderStatus[] QueueStatuses =
    {
        OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.Ready
    };

    public BarQueueService(AppDbContext db, IOptions<BarOptions> opt, ILogger<BarQueueService> logger)
    {
        _db = db;
        _opt = opt.Value;
        _logger = logger;
    }

    // ==========================================================================
    //  TÍNH THỜI GIAN PHA
    // ==========================================================================

    /// <summary>
    /// Thời gian pha một dòng món, tính bằng giây.
    /// <para>
    /// Ly đầu tính đủ, các ly sau của cùng món chỉ tính theo <c>BatchFactor</c>.
    /// VD 3 ly cà phê sữa đá (90 giây/ly, hệ số 0.55): 90 + 90×0.55×2 = 189 giây,
    /// chứ không phải 270 giây.
    /// </para>
    /// </summary>
    private int LineSeconds(int prepSeconds, int quantity)
    {
        if (quantity <= 0) return 0;
        var first = prepSeconds;
        var rest = (quantity - 1) * prepSeconds * _opt.BatchFactor;
        return (int)Math.Round(first + rest);
    }

    /// <summary>Làm tròn LÊN cho khách khỏi phải chờ lâu hơn lời hứa.</summary>
    private int RoundUp(int seconds)
    {
        var step = Math.Max(1, _opt.RoundToSeconds);
        return (int)Math.Ceiling(seconds / (double)step) * step;
    }

    public async Task<EtaEstimate> EstimateAsync(
        Guid storeId, IReadOnlyList<(Guid ProductId, int Quantity)> items, CancellationToken ct = default)
    {
        var stations = Math.Max(1, _opt.Stations);

        // ---- Việc còn tồn trong hàng pha ------------------------------------
        var queued = await _db.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.StoreId == storeId
                     && QueueStatuses.Contains(i.Order.Status))
            .Select(i => new { i.Quantity, Prep = i.Product!.PrepSeconds })
            .ToListAsync(ct);

        var workAhead = queued.Sum(q => LineSeconds(q.Prep, q.Quantity));
        var itemsAhead = queued.Sum(q => q.Quantity);

        // ---- Việc của chính đơn này -----------------------------------------
        var ids = items.Select(i => i.ProductId).Distinct().ToList();
        var prepMap = await _db.Products
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PrepSeconds, ct);

        var ownWork = items.Sum(i =>
            LineSeconds(prepMap.TryGetValue(i.ProductId, out var s) ? s : 90, i.Quantity));

        // ---- Ghép lại --------------------------------------------------------
        // Việc đang tồn chia đều cho các nhân viên; đơn mới phải chờ hết phần đó
        // rồi mới tới lượt. Cộng thêm thời gian đưa hàng cho khách.
        var total = (int)Math.Ceiling(workAhead / (double)stations)
                  + (int)Math.Ceiling(ownWork / (double)stations)
                  + _opt.HandoffSeconds;

        var rounded = RoundUp(total);

        return new EtaEstimate(
            TotalSeconds:     rounded,
            Minutes:          Math.Max(1, (int)Math.Ceiling(rounded / 60.0)),
            ReadyAtUtc:       DateTime.UtcNow.AddSeconds(rounded),
            QueuedItemsAhead: itemsAhead,
            WorkAheadSeconds: workAhead,
            OwnWorkSeconds:   ownWork,
            Stations:         stations);
    }

    // ==========================================================================
    //  HÀNG PHA + ĐÃ BÁN HÔM NAY
    // ==========================================================================

    public async Task<BarQueueSnapshot> GetSnapshotAsync(Guid storeId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var businessDate = VietnamTime.BusinessDate();
        var dayStart = VietnamTime.ToUtc(VietnamTime.Now().Date);
        var dayEnd = dayStart.AddDays(1);

        // ---- Hàng pha, đúng thứ tự đơn vào ----------------------------------
        // Sắp theo ConfirmedAt chứ không phải PlacedAt: đơn online đặt từ sáng
        // nhưng nhân viên vừa xác nhận thì nó đứng SAU đơn tại quầy đã nhận trước.
        // Ai vào hàng trước thì được pha trước — đó mới là thứ tự công bằng.
        var queueRows = await _db.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.StoreId == storeId
                     && QueueStatuses.Contains(i.Order.Status))
            .OrderBy(i => i.Order!.ConfirmedAt ?? i.Order.PlacedAt)
            .ThenBy(i => i.Order!.Code)
            .Select(i => new
            {
                i.OrderId,
                OrderItemId = i.Id,
                i.Order!.Code,
                i.Order.CustomerName,
                Channel = (int)i.Order.Channel,
                Status = (int)i.Order.Status,
                QueuedAt = i.Order.ConfirmedAt ?? i.Order.PlacedAt,
                i.Order.EstimatedReadyAt,
                i.ProductName,
                i.VariantName,
                ColorPrimaryHex = i.Product!.ColorPrimaryHex,
                ColorAccentHex = i.Product.ColorAccentHex,
                i.Product.ImageUrl,
                i.Quantity,
                i.Note,
                i.ModifiersJson
            })
            .ToListAsync(ct);

        var queue = queueRows.Select((r, idx) => new BarQueueLine(
            OrderId:          r.OrderId,
            OrderItemId:      r.OrderItemId,
            OrderCode:        r.Code,
            QueuePosition:    idx + 1,
            CustomerName:     r.CustomerName,
            Channel:          r.Channel,
            ProductName:      r.ProductName,
            VariantName:      r.VariantName,
            ColorPrimaryHex:  r.ColorPrimaryHex,
            ColorAccentHex:   r.ColorAccentHex,
            ImageUrl:         r.ImageUrl,
            Quantity:         r.Quantity,
            Note:             r.Note,
            Modifiers:        SummariseModifiers(r.ModifiersJson),
            Status:           r.Status,
            QueuedAt:         r.QueuedAt,
            EstimatedReadyAt: r.EstimatedReadyAt,
            WaitedSeconds:    (int)(now - r.QueuedAt).TotalSeconds,
            // Trễ hẹn = đã qua giờ đã hứa mà món vẫn chưa xong. Đây là dòng cần
            // nhân viên nhìn thấy trước tiên, nên phải đánh dấu riêng.
            IsLate:           r.EstimatedReadyAt.HasValue && now > r.EstimatedReadyAt.Value))
            .ToList();

        // ---- Đã bán hôm nay --------------------------------------------------
        // Chỉ tính đơn ĐÃ HOÀN TẤT. Đơn đang pha chưa phải là đã bán — tính vào
        // đây thì con số phồng lên rồi tụt xuống mỗi khi có đơn hủy.
        var soldRows = await _db.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.StoreId == storeId
                     && i.Order.Status == OrderStatus.Completed
                     && i.Order.CompletedAt >= dayStart
                     && i.Order.CompletedAt < dayEnd)
            .GroupBy(i => new { i.ProductId, i.ProductName, i.Product!.ColorPrimaryHex, i.Product.ImageUrl })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductName,
                g.Key.ColorPrimaryHex,
                g.Key.ImageUrl,
                Quantity = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal)
            })
            .ToListAsync(ct);

        var inQueue = queueRows
            .GroupBy(r => r.ProductName)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var soldToday = soldRows
            .Select(s => new SoldTodayLine(
                ProductId:       s.ProductId,
                ProductName:     s.ProductName,
                ColorPrimaryHex: s.ColorPrimaryHex,
                ImageUrl:        s.ImageUrl,
                QuantitySold:    s.Quantity,
                QuantityInQueue: inQueue.TryGetValue(s.ProductName, out var q) ? q : 0,
                Revenue:         s.Revenue))
            .OrderByDescending(s => s.QuantitySold)
            .ToList();

        // Món đang có trong hàng pha nhưng hôm nay chưa bán ly nào vẫn phải hiện,
        // nếu không nhân viên sẽ tưởng món đó không có ai gọi.
        foreach (var g in queueRows.GroupBy(r => new { r.ProductName, r.ColorPrimaryHex, r.ImageUrl }))
        {
            if (soldToday.Any(s => s.ProductName == g.Key.ProductName)) continue;
            soldToday.Add(new SoldTodayLine(
                Guid.Empty, g.Key.ProductName, g.Key.ColorPrimaryHex, g.Key.ImageUrl,
                0, g.Sum(x => x.Quantity), 0));
        }

        // ---- Số tổng của ngày -------------------------------------------------
        var dayOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.StoreId == storeId
                     && o.Status == OrderStatus.Completed
                     && o.CompletedAt >= dayStart && o.CompletedAt < dayEnd)
            .Select(o => o.GrandTotal)
            .ToListAsync(ct);

        var stations = Math.Max(1, _opt.Stations);
        var queueWork = queueRows.Sum(r => LineSeconds(90, r.Quantity)); // ước lượng thô cho dòng tổng

        return new BarQueueSnapshot(
            BusinessDate:          businessDate,
            Queue:                 queue,
            SoldToday:             soldToday,
            TotalCupsInQueue:      queueRows.Sum(r => r.Quantity),
            TotalCupsSoldToday:    soldRows.Sum(s => s.Quantity),
            TotalOrdersToday:      dayOrders.Count,
            RevenueToday:          dayOrders.Sum(),
            Stations:              stations,
            EstimatedClearSeconds: RoundUp((int)Math.Ceiling(queueWork / (double)stations)));
    }

    public async Task<Dictionary<Guid, int>> GetQueuedQuantitiesAsync(
        Guid storeId, CancellationToken ct = default)
    {
        return await _db.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.StoreId == storeId
                     && QueueStatuses.Contains(i.Order.Status))
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);
    }

    /// <summary>
    /// Gộp danh sách topping thành một dòng chữ ngắn cho màn hình pha chế.
    /// Nhân viên đứng pha cần đọc lướt, không cần cấu trúc JSON.
    /// </summary>
    private static string SummariseModifiers(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return "";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var names = doc.RootElement.EnumerateArray()
                .Select(e => e.TryGetProperty("name", out var n) ? n.GetString() : null)
                .Where(n => !string.IsNullOrEmpty(n));
            return string.Join(" · ", names);
        }
        catch (System.Text.Json.JsonException)
        {
            return "";
        }
    }
}
