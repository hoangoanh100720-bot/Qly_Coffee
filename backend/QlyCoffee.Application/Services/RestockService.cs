using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Application.Services;

// ==============================================================================
//  DANH SÁCH CẦN NHẬP HÀNG
// ==============================================================================
//
//  BÀI TOÁN
//  Trước đây muốn biết phải nhập gì, chủ quán phải mở trang kho rà từng dòng,
//  hoặc đợi tới lúc bấm "Nhận đơn" bị chặn — mà mỗi lần chặn chỉ báo MỘT nguyên
//  liệu. Bấm lại, lại báo nguyên liệu kế tiếp. Rất tốn thời gian.
//
//  Dịch vụ này làm bốn việc:
//
//    1. FindShortagesAsync   — kiểm tra một đơn, trả về TẤT CẢ nguyên liệu thiếu
//                              cùng lúc (không dừng ở cái đầu tiên).
//    2. RecordShortagesAsync — ghi lại nguyên liệu nào vừa chặn đơn, gộp theo ngày.
//    3. MarkRestockedAsync   — nhập hàng xong thì đóng việc lại.
//    4. GetRestockListAsync  — gom thành một danh sách cần nhập cho trang Kế hoạch
//                              và trang Nhập kho, kèm số lượng gợi ý.
//
//  MỘT NGUYÊN LIỆU VÀO DANH SÁCH KHI (sắp theo độ gấp):
//    0  Hết hàng — dùng được ≤ 0, và đang có món cần tới nó
//    1  Đã chặn đơn hôm nay — kể cả khi tồn kho chưa chạm ngưỡng nào
//    2  Dưới mức tồn tối thiểu
//    3  Chỉ đủ dùng khoảng một ngày theo tốc độ bán gần đây
//    4  Dưới điểm đặt hàng lại
//
//  NHẬP RỒI THÌ THÔI BÁO. Trạng thái 1 sống bằng nhật ký chặn đơn, mà nhật ký
//  thì không tự hết hạn: không đóng lại thì một lần chặn lúc 7 giờ sáng sẽ bắt
//  danh sách kêu "cần nhập" tới tận nửa đêm, kể cả khi hàng đã về từ 8 giờ.
//  Nhập kho (hoặc sơ chế) xong, MarkRestockedAsync đóng dòng nhật ký lại khi
//  lượng dùng được đã phủ hết chỗ từng thiếu; dòng đó chuyển sang DoneItems —
//  vẫn thấy được là "đã xong", nhưng không còn là việc phải làm. Các ngưỡng tồn
//  kho (0, 2, 3, 4) vẫn xét như thường: nhập một ít cho có mà vẫn dưới mức tối
//  thiểu thì dòng ở lại, vì lúc đó nó đúng là vẫn còn thiếu thật.
//
//  "DÙNG ĐƯỢC" = TỒN KHO − PHẦN CÁC ĐƠN ĐANG TRONG HÀNG PHA ĐÃ GIỮ.
//  Kho chỉ bị trừ lúc pha xong, nên nếu không trừ phần đang chờ thì danh sách
//  sẽ báo "còn đủ" cho thứ thực ra đã bị mười ly trong hàng chờ xí hết.
//
//  CHỈ NGUYÊN LIỆU THÔ vào danh sách mua. Bán thành phẩm (cốt trà, nước đường…)
//  nằm ở PrepItems riêng: thiếu chúng thì bếp phải sơ chế, không ai đi mua cả.
// ==============================================================================

public interface IRestockService
{
    /// <summary>Tất cả nguyên liệu không đủ cho một tập yêu cầu. Danh sách rỗng = đủ hết.</summary>
    /// <param name="countQueued">
    /// true (lúc NHẬN đơn): trừ phần các đơn khác trong hàng pha đã giữ.
    /// false (lúc HOÀN TẤT đơn): ly đã pha xong rồi, chỉ so với tồn kho thật.
    /// </param>
    Task<List<IngredientShortageDto>> FindShortagesAsync(
        Guid storeId, Guid? excludeOrderId,
        IReadOnlyList<IngredientRequirement> requirements,
        bool countQueued = true, CancellationToken ct = default);

    /// <summary>Phần nguyên liệu mà các đơn đang trong hàng pha sẽ dùng khi pha xong.</summary>
    Task<Dictionary<Guid, double>> GetQueuedDemandAsync(
        Guid storeId, Guid? excludeOrderId, CancellationToken ct = default);

    /// <summary>Ghi nhận nguyên liệu vừa chặn một lần nhận đơn. Không bao giờ ném lỗi.</summary>
    Task RecordShortagesAsync(
        Guid storeId, IReadOnlyList<IngredientShortageDto> shortages,
        IEnumerable<string> productNames, CancellationToken ct = default);

    /// <summary>
    /// Đánh dấu vừa nhập bù một nguyên liệu. Đóng việc lại nếu lượng dùng được
    /// đã phủ hết chỗ từng thiếu. Không bao giờ ném lỗi — nhập kho đã xong rồi,
    /// không được để việc dọn nhật ký làm hỏng nó.
    /// </summary>
    /// <returns>true nếu lần này việc chuyển sang xong.</returns>
    Task<bool> MarkRestockedAsync(
        Guid storeId, Guid ingredientId, double quantity, CancellationToken ct = default);

    /// <summary>Danh sách cần nhập của một ngày ("yyyy-MM-dd", giờ Việt Nam).</summary>
    Task<RestockListDto> GetRestockListAsync(
        Guid storeId, string? businessDate, CancellationToken ct = default);
}

/// <summary>Ném ra khi đơn không đủ nguyên liệu. Mang TOÀN BỘ danh sách thiếu.</summary>
public class IngredientShortageException : Exception
{
    public IReadOnlyList<IngredientShortageDto> Shortages { get; }

    public IngredientShortageException(IReadOnlyList<IngredientShortageDto> shortages)
        : base(BuildMessage(shortages))
        => Shortages = shortages;

    /// <summary>
    /// Câu tóm tắt cho những giao diện chỉ hiện được một dòng chữ.
    /// Vẫn liệt kê đủ — thông báo "thiếu sữa" rồi giấu mất "thiếu trân châu"
    /// chính là lỗi mà cả tính năng này sinh ra để sửa.
    /// </summary>
    public static string BuildMessage(IReadOnlyList<IngredientShortageDto> list)
    {
        if (list.Count == 0) return "Không đủ nguyên liệu.";

        var parts = list.Select(s =>
            $"{s.IngredientName} (thiếu {RestockService.FormatQty(s.Missing)} {s.UnitLabel})");

        return list.Count == 1
            ? $"Không đủ {parts.First()}."
            : $"Thiếu {list.Count} nguyên liệu: {string.Join(", ", parts)}.";
    }
}

public class RestockService : IRestockService
{
    private readonly AppDbContext _db;
    private readonly IRecipeService _recipe;
    private readonly PlanningOptions _opt;
    private readonly ILogger<RestockService> _logger;

    public RestockService(
        AppDbContext db,
        IRecipeService recipe,
        IOptions<PlanningOptions> opt,
        ILogger<RestockService> logger)
    {
        _db = db;
        _recipe = recipe;
        _opt = opt.Value;
        _logger = logger;
    }

    // ==========================================================================
    //  1. KIỂM TRA MỘT ĐƠN — TRẢ VỀ ĐỦ CẢ DANH SÁCH
    // ==========================================================================

    public async Task<List<IngredientShortageDto>> FindShortagesAsync(
        Guid storeId, Guid? excludeOrderId,
        IReadOnlyList<IngredientRequirement> requirements,
        bool countQueued = true, CancellationToken ct = default)
    {
        if (requirements.Count == 0) return new();

        var ids = requirements.Select(r => r.IngredientId).Distinct().ToList();

        // MỘT truy vấn cho mọi nguyên liệu. Bản cũ hỏi từng nguyên liệu một và
        // dừng ở cái thiếu đầu tiên — vừa chậm vừa giấu mất phần còn lại.
        var onHand = await OnHandAsync(storeId, ids, ct);
        var reserved = countQueued
            ? await GetQueuedDemandAsync(storeId, excludeOrderId, ct)
            : new Dictionary<Guid, double>();

        var info = await _db.Ingredients
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, i.Name, i.BaseUnit, i.IsPrepared })
            .ToDictionaryAsync(i => i.Id, ct);

        var result = new List<IngredientShortageDto>();

        foreach (var req in requirements)
        {
            var available = Math.Max(0,
                onHand.GetValueOrDefault(req.IngredientId) - reserved.GetValueOrDefault(req.IngredientId));

            // Sai số làm tròn của phép nhân định lượng không được chặn đơn
            if (available + 1e-6 >= req.Quantity) continue;

            info.TryGetValue(req.IngredientId, out var ing);

            result.Add(new IngredientShortageDto(
                req.IngredientId,
                ing?.Name ?? req.IngredientName,
                WasteRiskService.UnitLabel(ing?.BaseUnit ?? BaseUnit.Gram),
                ing?.IsPrepared ?? false,
                Round(req.Quantity),
                Round(available),
                Round(req.Quantity - available)));
        }

        // Nguyên liệu thô trước (việc đi mua lâu nhất), rồi thiếu nhiều trước
        return result
            .OrderBy(s => s.IsPrepared)
            .ThenByDescending(s => s.Available <= 0)
            .ThenBy(s => s.IngredientName)
            .ToList();
    }

    public async Task<Dictionary<Guid, double>> GetQueuedDemandAsync(
        Guid storeId, Guid? excludeOrderId, CancellationToken ct = default)
    {
        var queued = await _db.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.StoreId == storeId
                     && (excludeOrderId == null || i.OrderId != excludeOrderId)
                     && !i.Order.StockDeducted
                     && (i.Order.Status == OrderStatus.Confirmed
                      || i.Order.Status == OrderStatus.Preparing
                      || i.Order.Status == OrderStatus.Ready))
            .Select(i => new { i.ProductId, i.VariantId, i.ModifiersJson, i.Quantity })
            .ToListAsync(ct);

        if (queued.Count == 0) return new();

        var reqs = await _recipe.AggregateOrderAsync(
            queued.Select(q => new ExplodeRequest(
                q.ProductId, q.VariantId, OrderService.ParseModifierIds(q.ModifiersJson), q.Quantity)), ct);

        return reqs.ToDictionary(r => r.IngredientId, r => r.Quantity);
    }

    // ==========================================================================
    //  2. GHI NHẬN NGUYÊN LIỆU ĐÃ CHẶN ĐƠN
    // ==========================================================================

    public async Task RecordShortagesAsync(
        Guid storeId, IReadOnlyList<IngredientShortageDto> shortages,
        IEnumerable<string> productNames, CancellationToken ct = default)
    {
        if (shortages.Count == 0) return;

        var names = productNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();

        // Ghi nhật ký là việc PHỤ. Hỏng ở đây mà làm mất thông báo "thiếu nguyên
        // liệu" gửi về cho nhân viên thì còn tệ hơn không ghi — nên nuốt lỗi.
        try
        {
            await UpsertAsync(storeId, shortages, names, ct);
        }
        catch (DbUpdateException)
        {
            // Hai máy quầy cùng bị chặn một lúc, cả hai cùng chèn dòng mới cho
            // cùng (ngày, nguyên liệu) và một bên đâm vào chỉ mục duy nhất. Bỏ
            // các dòng vừa thêm khỏi bộ theo dõi rồi làm lại — lần này sẽ thấy
            // dòng của máy kia và cập nhật vào đó.
            foreach (var e in _db.ChangeTracker.Entries<StockShortageLog>().ToList())
                e.State = EntityState.Detached;

            try { await UpsertAsync(storeId, shortages, names, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Không ghi được nhật ký thiếu nguyên liệu"); }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không ghi được nhật ký thiếu nguyên liệu");
        }
    }

    private async Task UpsertAsync(
        Guid storeId, IReadOnlyList<IngredientShortageDto> shortages,
        List<string> names, CancellationToken ct)
    {
        var date = VietnamTime.Now().ToString("yyyy-MM-dd");
        var ids = shortages.Select(s => s.IngredientId).Distinct().ToList();

        var existing = await _db.StockShortageLogs
            .Where(l => l.StoreId == storeId && l.BusinessDate == date && ids.Contains(l.IngredientId))
            .ToDictionaryAsync(l => l.IngredientId, ct);

        foreach (var s in shortages.GroupBy(x => x.IngredientId).Select(g => g.First()))
        {
            if (!existing.TryGetValue(s.IngredientId, out var log))
            {
                log = new StockShortageLog
                {
                    StoreId      = storeId,
                    IngredientId = s.IngredientId,
                    BusinessDate = date
                };
                _db.StockShortageLogs.Add(log);
            }

            log.BlockedCount++;
            log.MaxMissingQuantity = Math.Max(log.MaxMissingQuantity, s.Missing);
            log.LastBlockedAt      = DateTime.UtcNow;
            log.UpdatedAt          = DateTime.UtcNow;
            log.AffectedProducts   = MergeNames(log.AffectedProducts, names);

            // Đã đánh dấu xong mà giờ lại chặn đơn nữa: hàng nhập về chưa đủ,
            // hoặc bán nhanh hơn dự tính. Mở việc ra lại, đừng để danh sách im
            // lặng đúng lúc nhân viên đang bị chặn không bán được.
            log.ResolvedAt = null;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Gộp tên món, bỏ trùng, cắt cho vừa cột 500 ký tự.</summary>
    private static string MergeNames(string current, List<string> add)
    {
        var all = current.Split(", ", StringSplitOptions.RemoveEmptyEntries)
            .Concat(add)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var joined = string.Join(", ", all);
        return joined.Length <= 500 ? joined : joined[..497] + "…";
    }

    // ==========================================================================
    //  3. NHẬP BÙ XONG — ĐÓNG VIỆC LẠI
    // ==========================================================================

    public async Task<bool> MarkRestockedAsync(
        Guid storeId, Guid ingredientId, double quantity, CancellationToken ct = default)
    {
        try
        {
            var date = VietnamTime.Now().ToString("yyyy-MM-dd");

            var log = await _db.StockShortageLogs.FirstOrDefaultAsync(
                l => l.StoreId == storeId
                  && l.BusinessDate == date
                  && l.IngredientId == ingredientId, ct);

            // Nhập hàng bình thường, hôm nay nó chưa chặn đơn nào — không có
            // việc nào để đóng. Các dòng vào danh sách vì ngưỡng tồn kho tự rời
            // đi khi tồn vượt ngưỡng, không cần đánh dấu gì.
            if (log is null || log.ResolvedAt is not null) return false;

            log.RestockedQuantity += Math.Max(0, quantity);
            log.UpdatedAt = DateTime.UtcNow;

            // Đóng việc theo TỒN KHO THẬT chứ không theo lượng vừa nhập: nhập
            // đủ số nhưng trong lúc đó hàng chờ pha đã xí hết thì vẫn còn thiếu.
            var onHand   = await OnHandAsync(storeId, new List<Guid> { ingredientId }, ct);
            var reserved = await GetQueuedDemandAsync(storeId, null, ct);
            var avail    = Math.Max(0, onHand.GetValueOrDefault(ingredientId)
                                     - reserved.GetValueOrDefault(ingredientId));

            var covered = avail + 1e-6 >= log.MaxMissingQuantity && avail > 0;
            if (covered) log.ResolvedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return covered;
        }
        catch (Exception ex)
        {
            // Hàng đã vào kho rồi. Cùng lắm là danh sách báo thừa một dòng đến
            // hết ngày — phiền, nhưng không được biến thành lỗi nhập kho.
            _logger.LogWarning(ex, "Không đóng được việc nhập bù cho nguyên liệu {Id}", ingredientId);
            return false;
        }
    }

    // ==========================================================================
    //  4. DANH SÁCH CẦN NHẬP
    // ==========================================================================

    public async Task<RestockListDto> GetRestockListAsync(
        Guid storeId, string? businessDate, CancellationToken ct = default)
    {
        var todayKey = VietnamTime.Now().ToString("yyyy-MM-dd");
        var date = string.IsNullOrWhiteSpace(businessDate) ? todayKey : businessDate;
        var isToday = date == todayKey;

        var ingredients = await _db.Ingredients
            .AsNoTracking()
            .Include(i => i.PurchaseUnits)
            .Where(i => i.StoreId == storeId && i.IsActive)
            .ToListAsync(ct);

        var ids = ingredients.Select(i => i.Id).ToList();

        var onHand   = await OnHandAsync(storeId, ids, ct);
        var reserved = await GetQueuedDemandAsync(storeId, null, ct);
        var usage    = await ForecastAllAsync(storeId, ct);
        var usedBy   = await ProductsUsingAsync(storeId, ct);

        var logs = await _db.StockShortageLogs
            .AsNoTracking()
            .Where(l => l.StoreId == storeId && l.BusinessDate == date)
            .ToDictionaryAsync(l => l.IngredientId, ct);

        var cover = Math.Max(1, _opt.RestockCoverDays);
        var rows = new List<RestockItemDto>();

        foreach (var ing in ingredients)
        {
            var stock  = onHand.GetValueOrDefault(ing.Id);
            var held   = reserved.GetValueOrDefault(ing.Id);
            var avail  = Math.Max(0, stock - held);
            var avg    = usage.GetValueOrDefault(ing.Id);
            double? days = avg > 0 ? avail / avg : null;
            logs.TryGetValue(ing.Id, out var log);
            var products = usedBy.GetValueOrDefault(ing.Id) ?? new List<string>();

            // Đã nhập bù đủ thì thôi báo. Dòng không biến mất hẳn — nó sang
            // DoneItems để màn hình nói được "xong việc", nhưng không còn là
            // thứ phải đi mua nữa.
            var isDone  = log?.ResolvedAt is not null;
            var blocked = log is not null && !isDone;

            // Ngày cũ: chỉ những gì đã THỰC SỰ chặn đơn hôm đó. Các ngưỡng tồn
            // kho là chuyện của hiện tại, gắn vào ngày cũ sẽ gây hiểu nhầm.
            int? status;
            if (!isToday)
                status = log is null ? null : isDone ? DoneStatus : 1;
            else
                // Hết sạch vẫn là hết sạch, kể cả khi sáng nay đã nhập một đợt:
                // ngưỡng 0 xét trước cờ "đã xong" chứ không bị nó che đi.
                status = avail <= 0 && products.Count > 0              ? 0
                       : blocked                                      ? 1
                       : ing.MinStockLevel > 0 && avail < ing.MinStockLevel ? 2
                       : days is < 1.5 && products.Count > 0           ? 3
                       : ing.ReorderPoint > 0 && avail < ing.ReorderPoint   ? 4
                       : isDone                                        ? DoneStatus
                       : null;

            if (status is null) continue;

            // --- Lượng gợi ý ---------------------------------------------------
            // Đưa tồn kho về: ngưỡng cao nhất đã cấu hình + đủ dùng `cover` ngày.
            // Và không bao giờ ít hơn lượng thiếu lớn nhất đã chặn đơn trong ngày —
            // trừ khi việc đã xong: lúc đó lấy nó làm đáy là gợi ý mua thêm lần nữa.
            var target = Math.Max(ing.ReorderPoint, ing.MinStockLevel) + avg * cover;
            var need   = Math.Max(target - avail, isDone ? 0 : log?.MaxMissingQuantity ?? 0);

            var unit = ing.PurchaseUnits
                .Where(p => p.ConversionQuantity > 0)
                .OrderByDescending(p => p.IsDefault)
                .FirstOrDefault();

            // Hết hàng mà chưa có định mức lẫn lịch sử: ít nhất gợi ý một quy cách
            // mua, để dòng này không hiện "nhập 0" — con số vô nghĩa nhất có thể.
            if (need <= 0 && status is 0 && unit is not null)
                need = unit.ConversionQuantity;

            var (qty, text) = RoundToPurchase(need, ing.BaseUnit, unit);

            var affected = log is not null && !string.IsNullOrEmpty(log.AffectedProducts)
                ? log.AffectedProducts
                : string.Join(", ", products.Take(4)) + (products.Count > 4 ? $" +{products.Count - 4} món" : "");

            rows.Add(new RestockItemDto(
                IngredientId:          ing.Id,
                Name:                  ing.Name,
                Sku:                   ing.Sku,
                CategoryLabel:         CategoryLabel(ing.Category),
                ColorHex:              ing.ColorHex,
                IconKey:               ing.IconKey,
                BaseUnit:              (int)ing.BaseUnit,
                UnitLabel:             WasteRiskService.UnitLabel(ing.BaseUnit),
                IsPrepared:            ing.IsPrepared,
                Status:                status.Value,
                StatusLabel:           StatusLabel(status.Value, isToday),
                OnHand:                Round(stock),
                Reserved:              Round(held),
                Available:             Round(avail),
                AvgDailyUsage:         Round(avg),
                DaysOfCover:           days is null ? null : Math.Round(days.Value, 1),
                MinStockLevel:         ing.MinStockLevel,
                ReorderPoint:          ing.ReorderPoint,
                SuggestedQuantity:     qty,
                SuggestedPurchaseText: text,
                AverageUnitCost:       ing.AverageUnitCost,
                EstimatedCost:         (int)Math.Round(qty * ing.AverageUnitCost),
                BlockedCount:          log?.BlockedCount ?? 0,
                MaxMissingQuantity:    Round(log?.MaxMissingQuantity ?? 0),
                AffectedProducts:      affected,
                IsDone:                status == DoneStatus,
                ResolvedAt:            log?.ResolvedAt,
                RestockedQuantity:     Round(log?.RestockedQuantity ?? 0)));
        }

        // Tách ngay sau vòng lặp: DoneItems không được lẫn vào phần đếm, phần
        // ước tính tiền hay câu chép gửi nhà cung cấp.
        var done = rows.Where(r => r.IsDone).OrderBy(r => r.Name).ToList();
        rows = rows.Where(r => !r.IsDone).ToList();

        var ordered = rows
            .OrderBy(r => r.Status)
            .ThenByDescending(r => r.BlockedCount)
            .ThenBy(r => r.DaysOfCover ?? double.MaxValue)
            .ThenBy(r => r.Name)
            .ToList();

        var raw  = ordered.Where(r => !r.IsPrepared).ToList();
        var prep = ordered.Where(r => r.IsPrepared).ToList();

        return new RestockListDto(
            BusinessDate:           date,
            IsToday:                isToday,
            ComputedAt:             DateTime.UtcNow,
            CoverDays:              cover,
            Items:                  raw,
            PrepItems:              prep,
            TotalEstimatedCost:     raw.Sum(r => r.EstimatedCost),
            BlockedIngredientCount: ordered.Count(r => r.BlockedCount > 0),
            DoneItems:              done);
    }

    // ==========================================================================
    //  TRUY VẤN GỘP
    // ==========================================================================

    private async Task<Dictionary<Guid, double>> OnHandAsync(
        Guid storeId, List<Guid> ids, CancellationToken ct)
        => await _db.InventoryLots
            .AsNoTracking()
            .Where(l => l.StoreId == storeId
                     && ids.Contains(l.IngredientId)
                     && l.Status == LotStatus.Active
                     && l.RemainingQuantity > 0)
            .GroupBy(l => l.IngredientId)
            .Select(g => new { g.Key, Qty = g.Sum(l => (double)l.RemainingQuantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    /// <summary>
    /// Tiêu thụ bình quân mỗi ngày của mọi nguyên liệu, cùng công thức EWMA với
    /// WasteRiskService.ForecastDailyUsageAsync nhưng đọc lịch sử một lần cho cả
    /// kho thay vì một truy vấn cho mỗi nguyên liệu.
    /// </summary>
    private async Task<Dictionary<Guid, double>> ForecastAllAsync(Guid storeId, CancellationToken ct)
    {
        var today = VietnamTime.Now().Date;
        var start = today.AddDays(-_opt.ForecastLookbackDays);
        var since = start.ToString("yyyy-MM-dd");

        var history = await _db.DailyConsumptions
            .AsNoTracking()
            .Where(c => c.StoreId == storeId && string.Compare(c.BusinessDate, since) >= 0)
            .Select(c => new { c.IngredientId, c.BusinessDate, c.QuantityUsed })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, double>();

        foreach (var g in history.GroupBy(h => h.IngredientId))
        {
            var byDate = g.GroupBy(x => x.BusinessDate).ToDictionary(x => x.Key, x => x.Sum(y => y.QuantityUsed));

            // Ngày không bán vẫn phải tính là 0, bỏ qua thì EWMA bị thổi phồng.
            double? ewma = null;
            for (var d = start; d < today; d = d.AddDays(1))
            {
                var v = byDate.GetValueOrDefault(d.ToString("yyyy-MM-dd"));
                ewma = ewma is null ? v : _opt.ForecastEwmaAlpha * v + (1 - _opt.ForecastEwmaAlpha) * ewma.Value;
            }

            result[g.Key] = Math.Round(ewma ?? 0, 4);
        }

        return result;
    }

    /// <summary>
    /// Nguyên liệu → tên các món đang bán cần tới nó (qua công thức, topping,
    /// hoặc gián tiếp qua bán thành phẩm được sơ chế từ nó).
    /// </summary>
    private async Task<Dictionary<Guid, List<string>>> ProductsUsingAsync(Guid storeId, CancellationToken ct)
    {
        var direct = await _db.RecipeItems
            .AsNoTracking()
            .Where(r => r.Product!.StoreId == storeId && r.Product.IsActive)
            .Select(r => new { r.IngredientId, r.Product!.Name })
            .ToListAsync(ct);

        var viaModifier = await (
            from mr in _db.ModifierRecipeItems.AsNoTracking()
            join m in _db.Modifiers.AsNoTracking() on mr.ModifierId equals m.Id
            join pm in _db.ProductModifiers.AsNoTracking() on m.ModifierGroupId equals pm.ModifierGroupId
            join p in _db.Products.AsNoTracking() on pm.ProductId equals p.Id
            where p.StoreId == storeId && p.IsActive
            select new { mr.IngredientId, p.Name })
            .ToListAsync(ct);

        var map = direct.Concat(viaModifier)
            .GroupBy(x => x.IngredientId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).Distinct().OrderBy(n => n).ToList());

        // Nguyên liệu thô chỉ dùng để nấu bán thành phẩm (đường → nước đường)
        // thì không xuất hiện trong công thức món nào. Kế thừa danh sách món của
        // bán thành phẩm, nếu không nó sẽ bị coi là "không ai dùng" và bị bỏ qua
        // ngay cả khi đã hết sạch.
        var prepLines = await _db.PrepRecipeLines
            .AsNoTracking()
            .Where(l => l.PrepRecipe!.StoreId == storeId && l.PrepRecipe.IsActive)
            .Select(l => new { l.IngredientId, l.PrepRecipe!.OutputIngredientId })
            .ToListAsync(ct);

        foreach (var line in prepLines)
        {
            if (!map.TryGetValue(line.OutputIngredientId, out var viaPrep)) continue;

            map[line.IngredientId] = map.TryGetValue(line.IngredientId, out var own)
                ? own.Concat(viaPrep).Distinct().OrderBy(n => n).ToList()
                : viaPrep;
        }

        return map;
    }

    // ==========================================================================
    //  TIỆN ÍCH
    // ==========================================================================

    /// <summary>
    /// Làm tròn LÊN theo cách người ta thật sự đi mua.
    /// Có quy cách mua (thùng, bao) thì tròn theo số thùng; không có thì tròn
    /// 50 g/ml cho lượng nhỏ và nửa kg/lít cho lượng lớn — không ai gọi nhà
    /// cung cấp đặt 1.237 g đường.
    /// </summary>
    internal static (double Qty, string Text) RoundToPurchase(double need, BaseUnit unit, PurchaseUnit? pu)
    {
        if (need <= 0)
            return (0, "Chưa có định mức — tự quyết số lượng");

        if (pu is not null)
        {
            var n = Math.Ceiling(need / pu.ConversionQuantity - 1e-9);
            return (n * pu.ConversionQuantity, $"{n:0} × {pu.Name}");
        }

        switch (unit)
        {
            case BaseUnit.Gram:
            case BaseUnit.Milliliter:
                var big = unit == BaseUnit.Gram ? "kg" : "lít";
                var small = unit == BaseUnit.Gram ? "g" : "ml";
                if (need >= 1000)
                {
                    var q = Math.Ceiling(need / 500 - 1e-9) * 500;
                    return (q, $"{FormatQty(q / 1000)} {big}");
                }
                var s = Math.Ceiling(need / 50 - 1e-9) * 50;
                return (s, $"{s:0} {small}");

            default:
                var c = Math.Ceiling(need - 1e-9);
                return (c, $"{c:0} cái");
        }
    }

    /// <summary>Số lượng kiểu Việt: dấu phẩy thập phân, bỏ số 0 thừa.</summary>
    internal static string FormatQty(double v)
        => (v >= 100 ? v.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.')
                     : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ','));

    private static double Round(double v) => Math.Round(v, 2);

    /// <summary>Mã trạng thái "đã nhập bù xong". Để cuối dải vì nó không phải việc phải làm.</summary>
    internal const int DoneStatus = 9;

    private static string StatusLabel(int status, bool isToday) => status switch
    {
        0 => "Hết hàng",
        1 => isToday ? "Đã chặn đơn hôm nay" : "Đã chặn đơn ngày này",
        2 => "Dưới mức tối thiểu",
        3 => "Chỉ đủ dùng khoảng 1 ngày",
        4 => "Dưới điểm đặt hàng",
        DoneStatus => "Đã nhập bù đủ",
        _ => ""
    };

    private static string CategoryLabel(IngredientCategory c) => c switch
    {
        IngredientCategory.Coffee    => "Cà phê",
        IngredientCategory.Tea       => "Trà",
        IngredientCategory.Dairy     => "Sữa & kem",
        IngredientCategory.Syrup     => "Siro & sốt",
        IngredientCategory.Fruit     => "Trái cây",
        IngredientCategory.Topping   => "Topping",
        IngredientCategory.Powder    => "Bột",
        IngredientCategory.Sweetener => "Chất tạo ngọt",
        IngredientCategory.Packaging => "Bao bì",
        IngredientCategory.Bakery    => "Bánh & đồ ăn",
        _                            => "Khác"
    };
}
