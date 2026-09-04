using QlyCoffee.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;

namespace QlyCoffee.Application.Services;

// ==============================================================================
//  DỊCH VỤ KHO — TRÁI TIM CỦA HỆ THỐNG
//
//  Đây là file dễ gây thiệt hại nhất nếu sửa sai. Ba quy tắc bất di bất dịch:
//
//  1. MỌI THAO TÁC KHO PHẢI NẰM TRONG TRANSACTION
//     Nếu tạo đơn thành công mà trừ kho thất bại (hoặc ngược lại), số liệu
//     hỏng vĩnh viễn và không có cách nào dò ra.
//
//  2. PHẢI KHÓA HÀNG KHI ĐỌC LÔ (SELECT ... FOR UPDATE)
//     Hai thu ngân bấm xác nhận cùng lúc trên ly cuối cùng: không khóa thì cả
//     hai cùng đọc "còn 1", cùng trừ, kết quả tồn kho âm.
//
//  3. PHẢI CÓ KHÓA CHỐNG GHI TRÙNG (idempotency key)
//     Mạng chập chờn, client gửi lại yêu cầu, kho bị trừ hai lần. Khóa duy nhất
//     ở cột idempotency_key là chốt chặn cuối cùng ở tầng database.
// ==============================================================================

public interface IInventoryService
{
    Task<double> GetAvailableStockAsync(Guid ingredientId, CancellationToken ct = default);
    Task<ConsumeResult> ConsumeFefoAsync(AppDbContext tx, ConsumeRequest request, CancellationToken ct = default);
    Task<InventoryLot> ReceiveStockAsync(ReceiveStockCommand cmd, CancellationToken ct = default);
    Task ReturnStockForOrderAsync(AppDbContext tx, Guid orderId, Guid actorId, CancellationToken ct = default);
    Task<int> ExpireOverdueLotsAsync(Guid storeId, CancellationToken ct = default);
    Task RecordWasteAsync(WasteCommand cmd, CancellationToken ct = default);
    Task RecalculateAverageCostAsync(Guid ingredientId, CancellationToken ct = default);
}

/// <summary>
/// Yêu cầu tiêu thụ nguyên liệu từ kho.
/// <para>
/// <c>Type</c> là loại bút toán ghi vào sổ cái, mặc định <c>SaleOut</c> — bán một
/// ly cho khách. Sơ chế PHẢI truyền <c>ProductionOut</c>: lá trà rời kho để thành
/// cốt trà thì không phải doanh thu, và giá vốn hàng bán không được tính nó hai
/// lần (một lần lúc ủ, một lần nữa lúc bán ly trà làm từ cốt đó).
/// </para>
/// </summary>
public record ConsumeRequest(
    Guid StoreId,
    Guid IngredientId,
    double Quantity,
    string ReferenceType,
    Guid ReferenceId,
    string IdempotencyKey,
    Guid? ActorUserId = null,
    MovementType Type = MovementType.SaleOut);

/// <summary>Kết quả tiêu thụ: tổng giá vốn thực tế và các lô đã bị trừ.</summary>
public record ConsumeResult(int TotalCost, IReadOnlyList<(Guid LotId, double Quantity)> ConsumedLots);

public record ReceiveStockCommand(
    Guid StoreId,
    Guid IngredientId,
    double Quantity,
    Guid? PurchaseUnitId,
    int UnitCost,
    DateTime? ExpiryDate,
    string? LotCode,
    Guid? SupplierId,
    string? Note,
    Guid ActorUserId);

public record WasteCommand(
    Guid StoreId,
    Guid IngredientId,
    Guid? LotId,
    double Quantity,
    string Reason,
    Guid ActorUserId);

/// <summary>Ném ra khi tồn kho không đủ. Mang theo số liệu để giao diện báo cụ thể.</summary>
public class InsufficientStockException : Exception
{
    public Guid IngredientId { get; }
    public string IngredientName { get; }
    public double Required { get; }
    public double Available { get; }

    public InsufficientStockException(Guid ingredientId, string name, double required, double available)
        : base($"Không đủ {name}: cần {required:0.##}, chỉ còn {available:0.##}")
    {
        IngredientId = ingredientId;
        IngredientName = name;
        Required = required;
        Available = available;
    }
}

public class InventoryService : IInventoryService
{
    private readonly AppDbContext _db;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(AppDbContext db, ILogger<InventoryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ==========================================================================
    //  ĐỌC TỒN KHO
    // ==========================================================================

    /// <summary>
    /// Tồn kho khả dụng = tổng lượng còn lại của các lô đang Active.
    /// Lô đã hết hạn KHÔNG được tính vào dù vẫn còn số lượng.
    /// </summary>
    public async Task<double> GetAvailableStockAsync(Guid ingredientId, CancellationToken ct = default)
        => await _db.InventoryLots
            .Where(l => l.IngredientId == ingredientId
                     && l.Status == LotStatus.Active
                     && l.RemainingQuantity > 0)
            .SumAsync(l => l.RemainingQuantity, ct);

    // ==========================================================================
    //  TIÊU THỤ THEO FEFO  ⭐ HÀM QUAN TRỌNG NHẤT
    // ==========================================================================

    /// <summary>
    /// Trừ kho theo nguyên tắc FEFO (First Expired, First Out — lô hết hạn sớm
    /// nhất được dùng trước).
    /// <para>
    /// Vì sao FEFO chứ không phải FIFO: quán nhập lô mới có hạn dài, lô cũ có
    /// hạn ngắn. Dùng FIFO theo ngày nhập thì lô hạn ngắn nằm lại và hỏng.
    /// FEFO chính là thứ làm cho tính năng cảnh báo cận hạn có ý nghĩa.
    /// </para>
    /// <para>
    /// PHẢI gọi bên trong một transaction đang mở. Hàm này không tự mở transaction
    /// vì một đơn hàng cần trừ nhiều nguyên liệu — tất cả phải cùng thành công
    /// hoặc cùng thất bại.
    /// </para>
    /// </summary>
    public async Task<ConsumeResult> ConsumeFefoAsync(
        AppDbContext tx, ConsumeRequest req, CancellationToken ct = default)
    {
        // ---- Chốt chặn 1: đã xử lý yêu cầu này rồi thì thoát ngay ----------
        // Xảy ra khi client gửi lại do timeout mạng. Trả về kết quả cũ, không trừ thêm.
        var existing = await tx.StockMovements
            .Where(m => m.IdempotencyKey != null && m.IdempotencyKey.StartsWith(req.IdempotencyKey))
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            _logger.LogInformation(
                "Bỏ qua yêu cầu trùng lặp {Key} — đã xử lý trước đó", req.IdempotencyKey);
            return new ConsumeResult(
                existing.Sum(m => m.TotalCost),
                existing.Where(m => m.LotId.HasValue)
                        .Select(m => (m.LotId!.Value, Math.Abs(m.QuantityDelta)))
                        .ToList());
        }

        var ingredient = await tx.Ingredients
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == req.IngredientId, ct)
            ?? throw new BusinessRuleException($"Không tìm thấy nguyên liệu {req.IngredientId}");

        // ---- Đọc lô theo thứ tự FEFO, CÓ KHÓA HÀNG -------------------------
        //
        // FOR UPDATE khóa các dòng cho tới hết transaction. Giao dịch khác đọc
        // cùng những lô này sẽ phải chờ. Đây là thứ ngăn tồn kho âm khi hai đơn
        // cùng lấy lô cuối cùng.
        //
        // NULLS LAST: lô không có hạn sử dụng (đá viên, ly nhựa) xếp sau cùng —
        // ưu tiên dùng hết thứ sẽ hỏng trước.
        //
        // Dùng SQL thô vì EF Core chưa hỗ trợ FOR UPDATE ở LINQ.
        var lots = await tx.InventoryLots
            .FromSqlRaw("""
                SELECT * FROM inventory_lots
                WHERE store_id = {0}
                  AND ingredient_id = {1}
                  AND status = 0
                  AND remaining_quantity > 0
                  AND deleted_at IS NULL
                ORDER BY expiry_date ASC NULLS LAST, received_at ASC
                FOR UPDATE
                """, req.StoreId, req.IngredientId)
            .ToListAsync(ct);

        var available = lots.Sum(l => l.RemainingQuantity);

        // ---- Chốt chặn 2: không đủ thì hủy toàn bộ, không trừ lô nào -------
        if (available < req.Quantity)
            throw new InsufficientStockException(
                req.IngredientId, ingredient.Name, req.Quantity, available);

        // ---- Trừ dần qua từng lô ------------------------------------------
        var remaining = Round(req.Quantity);
        var totalCost = 0;
        var consumed = new List<(Guid, double)>();

        foreach (var lot in lots)
        {
            if (remaining <= 0) break;

            var take = Math.Min(lot.RemainingQuantity, remaining);
            var newRemaining = Round(lot.RemainingQuantity - take);

            lot.RemainingQuantity = newRemaining;
            // Lô cạn thì chuyển trạng thái để lần sau không phải quét qua nữa
            lot.Status = newRemaining <= 0.0001 ? LotStatus.Depleted : LotStatus.Active;
            lot.UpdatedAt = DateTime.UtcNow;

            var lineCost = (int)Math.Round(take * lot.UnitCost);

            tx.StockMovements.Add(new StockMovement
            {
                StoreId       = req.StoreId,
                IngredientId  = req.IngredientId,
                LotId         = lot.Id,
                Type          = req.Type,
                QuantityDelta = -take,               // ÂM vì là xuất kho
                UnitCost      = lot.UnitCost,
                TotalCost     = lineCost,
                ReferenceType = req.ReferenceType,
                ReferenceId   = req.ReferenceId,
                // Khóa phải duy nhất theo TỪNG LÔ, vì một yêu cầu có thể trừ nhiều lô
                IdempotencyKey = $"{req.IdempotencyKey}:LOT:{lot.Id}",
                ActorUserId   = req.ActorUserId,
                OccurredAt    = DateTime.UtcNow
            });

            totalCost += lineCost;
            remaining = Round(remaining - take);
            consumed.Add((lot.Id, take));
        }

        return new ConsumeResult(totalCost, consumed);
    }

    // ==========================================================================
    //  HOÀN KHO KHI HỦY ĐƠN
    // ==========================================================================

    /// <summary>
    /// Trả nguyên liệu về ĐÚNG những lô đã bị trừ.
    /// <para>
    /// Không thể chỉ cộng vào lô bất kỳ, vì mỗi lô có hạn dùng và giá vốn riêng.
    /// Cộng nhầm lô sẽ làm sai giá vốn hàng bán và sai cảnh báo hạn dùng.
    /// </para>
    /// </summary>
    public async Task ReturnStockForOrderAsync(
        AppDbContext tx, Guid orderId, Guid actorId, CancellationToken ct = default)
    {
        var saleMovements = await tx.StockMovements
            .Where(m => m.ReferenceType == "ORDER"
                     && m.ReferenceId == orderId
                     && m.Type == MovementType.SaleOut)
            .ToListAsync(ct);

        foreach (var m in saleMovements)
        {
            if (m.LotId is null) continue;

            var qty = Math.Abs(m.QuantityDelta);

            var lot = await tx.InventoryLots.FirstOrDefaultAsync(l => l.Id == m.LotId, ct);
            if (lot is null) continue;

            lot.RemainingQuantity = Round(lot.RemainingQuantity + qty);
            // Đánh thức lô đã cạn. Nhưng nếu lô đã quá hạn thì KHÔNG cho dùng lại —
            // hàng trả về vẫn là hàng hết hạn.
            if (lot.Status == LotStatus.Depleted && lot.ExpiryDate > DateTime.UtcNow)
                lot.Status = LotStatus.Active;
            lot.UpdatedAt = DateTime.UtcNow;

            tx.StockMovements.Add(new StockMovement
            {
                StoreId        = m.StoreId,
                IngredientId   = m.IngredientId,
                LotId          = m.LotId,
                Type           = MovementType.ReturnIn,
                QuantityDelta  = qty,                 // DƯƠNG vì là nhập lại
                UnitCost       = m.UnitCost,
                TotalCost      = m.TotalCost,
                ReferenceType  = "ORDER",
                ReferenceId    = orderId,
                Reason         = "Hoàn kho do hủy đơn",
                IdempotencyKey = $"RETURN:{m.Id}",
                ActorUserId    = actorId,
                OccurredAt     = DateTime.UtcNow
            });
        }
    }

    // ==========================================================================
    //  NHẬP KHO
    // ==========================================================================

    public async Task<InventoryLot> ReceiveStockAsync(
        ReceiveStockCommand cmd, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var ingredient = await _db.Ingredients
            .FirstOrDefaultAsync(i => i.Id == cmd.IngredientId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy nguyên liệu");

        // ---- Quy đổi về đơn vị cơ sở --------------------------------------
        // Người dùng nhập "3 thùng", hệ thống lưu 36000 ml.
        var baseQuantity = cmd.Quantity;
        if (cmd.PurchaseUnitId is Guid puId)
        {
            var pu = await _db.PurchaseUnits.FirstOrDefaultAsync(p => p.Id == puId, ct);
            if (pu is not null) baseQuantity = cmd.Quantity * pu.ConversionQuantity;
        }
        baseQuantity = Round(baseQuantity);

        if (baseQuantity <= 0)
            throw new BusinessRuleException("Số lượng nhập phải lớn hơn 0");

        // ---- Hạn sử dụng ---------------------------------------------------
        // Ưu tiên hạn ghi trên bao bì; không có thì suy từ số ngày mặc định.
        var expiry = cmd.ExpiryDate
            ?? (ingredient.DefaultShelfLifeDays is int days
                ? DateTime.UtcNow.Date.AddDays(days)
                : null);

        // Mã lô là DUY NHẤT trong một cửa hàng — đó là điều kiện để truy ngược
        // được "lô nào đã bị bán vào những đơn nào". Kiểm ở đây để nhân viên
        // nhận được câu tiếng Việt rõ ràng, thay vì để ràng buộc unique của
        // database bắn ra một DbUpdateException lộ nguyên stack trace lên màn hình.
        var lotCode = cmd.LotCode?.Trim();
        if (!string.IsNullOrEmpty(lotCode))
        {
            var taken = await _db.InventoryLots
                .AnyAsync(l => l.StoreId == cmd.StoreId && l.LotCode == lotCode, ct);

            if (taken)
                throw new BusinessRuleException(
                    $"Mã lô \"{lotCode}\" đã được dùng cho một lô khác. " +
                    "Mỗi lô phải có mã riêng để truy ngược được nguyên liệu đã bán đi đâu. " +
                    "Để trống ô mã lô thì hệ thống tự sinh mã.");
        }

        var lot = new InventoryLot
        {
            StoreId          = cmd.StoreId,
            IngredientId     = cmd.IngredientId,
            LotCode          = string.IsNullOrEmpty(lotCode) ? GenerateLotCode(ingredient.Sku) : lotCode,
            ReceivedQuantity = baseQuantity,
            RemainingQuantity = baseQuantity,
            UnitCost         = cmd.UnitCost,
            ReceivedAt       = DateTime.UtcNow,
            ExpiryDate       = expiry,
            Status           = LotStatus.Active,
            SupplierId       = cmd.SupplierId,
            Note             = cmd.Note
        };

        _db.InventoryLots.Add(lot);

        _db.StockMovements.Add(new StockMovement
        {
            StoreId        = cmd.StoreId,
            IngredientId   = cmd.IngredientId,
            LotId          = lot.Id,
            Type           = MovementType.PurchaseIn,
            QuantityDelta  = baseQuantity,            // DƯƠNG
            UnitCost       = cmd.UnitCost,
            TotalCost      = (int)Math.Round(baseQuantity * cmd.UnitCost),
            ReferenceType  = "PURCHASE",
            ReferenceId    = lot.Id,
            IdempotencyKey = $"RECEIVE:{lot.Id}",
            ActorUserId    = cmd.ActorUserId,
            OccurredAt     = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        // Giá vốn bình quân đổi → giá vốn mọi món dùng nguyên liệu này cũng đổi
        await UpdateWeightedAverageCostAsync(cmd.IngredientId, ct);

        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Nhập kho {Qty} {Unit} {Name}, lô {LotCode}, hạn {Expiry}",
            baseQuantity, ingredient.BaseUnit, ingredient.Name, lot.LotCode, expiry);

        return lot;
    }

    /// <summary>
    /// Cập nhật giá vốn bình quân gia quyền trên các lô còn hàng.
    /// <para>
    /// Công thức: Σ(còn lại × giá lô) / Σ(còn lại).
    /// Dùng bình quân gia quyền thay vì giá lô mới nhất để giá vốn món không
    /// nhảy vọt mỗi lần nhập hàng giá cao.
    /// </para>
    /// <para>
    /// Công khai vì sơ chế cũng làm đổi giá vốn: mẻ cốt trà mới đắt hơn mẻ cũ thì
    /// giá vốn ly trà sữa phải đổi theo, y như khi nhập lá trà giá mới.
    /// </para>
    /// </summary>
    public Task RecalculateAverageCostAsync(Guid ingredientId, CancellationToken ct = default)
        => UpdateWeightedAverageCostAsync(ingredientId, ct);

    private async Task UpdateWeightedAverageCostAsync(Guid ingredientId, CancellationToken ct)
    {
        var lots = await _db.InventoryLots
            .Where(l => l.IngredientId == ingredientId
                     && l.Status == LotStatus.Active
                     && l.RemainingQuantity > 0)
            .Select(l => new { l.RemainingQuantity, l.UnitCost })
            .ToListAsync(ct);

        if (lots.Count == 0) return;

        var totalQty = lots.Sum(l => l.RemainingQuantity);
        if (totalQty <= 0) return;

        var totalValue = lots.Sum(l => l.RemainingQuantity * l.UnitCost);
        var avg = (int)Math.Round(totalValue / totalQty);

        await _db.Ingredients
            .Where(i => i.Id == ingredientId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.AverageUnitCost, avg), ct);
    }

    // ==========================================================================
    //  HAO HỤT & HẾT HẠN
    // ==========================================================================

    /// <summary>
    /// Ghi nhận hao hụt (pha hỏng, rơi vãi, đổ bỏ).
    /// Bắt buộc có lý do — không cho phép giảm kho mà không giải trình.
    /// </summary>
    public async Task RecordWasteAsync(WasteCommand cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            throw new BusinessRuleException("Phải nhập lý do khi ghi nhận hao hụt");

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // Chỉ định lô cụ thể thì trừ đúng lô đó; không thì theo FEFO như bán hàng
        if (cmd.LotId is Guid lotId)
        {
            var lot = await _db.InventoryLots.FirstOrDefaultAsync(l => l.Id == lotId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy lô hàng");

            if (lot.RemainingQuantity < cmd.Quantity)
                throw new BusinessRuleException(
                    $"Lô {lot.LotCode} chỉ còn {lot.RemainingQuantity:0.##}");

            lot.RemainingQuantity = Round(lot.RemainingQuantity - cmd.Quantity);
            if (lot.RemainingQuantity <= 0.0001) lot.Status = LotStatus.Depleted;

            _db.StockMovements.Add(new StockMovement
            {
                StoreId        = cmd.StoreId,
                IngredientId   = cmd.IngredientId,
                LotId          = lot.Id,
                Type           = MovementType.Waste,
                QuantityDelta  = -cmd.Quantity,
                UnitCost       = lot.UnitCost,
                TotalCost      = (int)Math.Round(cmd.Quantity * lot.UnitCost),
                ReferenceType  = "WASTE",
                Reason         = cmd.Reason,
                IdempotencyKey = $"WASTE:{GuidV7.New()}",
                ActorUserId    = cmd.ActorUserId,
                OccurredAt     = DateTime.UtcNow
            });
        }
        else
        {
            await ConsumeFefoAsync(_db, new ConsumeRequest(
                cmd.StoreId, cmd.IngredientId, cmd.Quantity,
                "WASTE", GuidV7.New(),
                $"WASTE:{GuidV7.New()}", cmd.ActorUserId), ct);
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Đánh dấu các lô đã quá hạn và ghi bút toán tiêu hủy.
    /// <para>
    /// HAI LOẠI HẠN, HAI CÁCH TÍNH — đây là chỗ dễ hiểu nhầm nhất của hàm này:
    /// </para>
    /// <para>
    /// · Lô MUA VỀ có hạn ghi theo NGÀY, giờ đúng 00:00. "Hạn 15/08" nghĩa là
    ///   dùng được HẾT ngày 15/08, nên chỉ quá hạn từ 16/08.
    /// </para>
    /// <para>
    /// · Lô SƠ CHẾ có hạn ghi theo GIỜ ("14:30 hôm nay"). Mẻ cốt trà ủ lúc 08:30
    ///   hạn 6 tiếng thì 14:30 là hỏng, không phải hết ngày. Áp quy tắc theo ngày
    ///   cho nó nghĩa là cho phép bán trà thiu suốt buổi chiều.
    /// </para>
    /// <para>
    /// Job buổi sáng gọi hàm này trước khi quán mở cửa, và chu kỳ ngắn trong ngày
    /// gọi lại để bắt kịp các mẻ sơ chế — xem <c>ScheduledJobsService</c>.
    /// Hàm chạy lại bao nhiêu lần cũng vô hại: lô đã chuyển Expired không lọt vào
    /// truy vấn nữa.
    /// </para>
    /// </summary>
    public async Task<int> ExpireOverdueLotsAsync(Guid storeId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        // Lọc thô ở database bằng điều kiện RỘNG NHẤT (quá hạn theo giờ), rồi
        // lọc tinh trong bộ nhớ. Tập lô còn hạn dùng của một quán chỉ vài chục
        // dòng nên rẻ, mà tránh phải viết biểu thức ngày-giờ khó đọc trong SQL.
        var candidates = await _db.InventoryLots
            .Where(l => l.StoreId == storeId
                     && l.Status == LotStatus.Active
                     && l.RemainingQuantity > 0
                     && l.ExpiryDate != null
                     && l.ExpiryDate < now)
            .ToListAsync(ct);

        var overdue = candidates
            .Where(l =>
            {
                var expiry = l.ExpiryDate!.Value;

                // Có phần giờ phút = hạn của mẻ sơ chế → so tới từng phút.
                // Đúng 00:00 = hạn theo ngày → còn dùng được hết ngày hôm đó.
                return expiry.TimeOfDay != TimeSpan.Zero
                    ? expiry < now
                    : expiry.Date < today;
            })
            .ToList();

        if (overdue.Count == 0) return 0;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        foreach (var lot in overdue)
        {
            var qty = lot.RemainingQuantity;

            _db.StockMovements.Add(new StockMovement
            {
                StoreId        = lot.StoreId,
                IngredientId   = lot.IngredientId,
                LotId          = lot.Id,
                Type           = MovementType.ExpiredOut,
                QuantityDelta  = -qty,
                UnitCost       = lot.UnitCost,
                TotalCost      = (int)Math.Round(qty * lot.UnitCost),
                ReferenceType  = "EXPIRY",
                ReferenceId    = lot.Id,
                Reason         = lot.ExpiryDate!.Value.TimeOfDay != TimeSpan.Zero
                                     ? $"Quá hạn lúc {lot.ExpiryDate:HH:mm dd/MM/yyyy}"
                                     : $"Quá hạn sử dụng ngày {lot.ExpiryDate:dd/MM/yyyy}",
                IdempotencyKey = $"EXPIRE:{lot.Id}",
                OccurredAt     = DateTime.UtcNow
            });

            lot.RemainingQuantity = 0;
            lot.Status = LotStatus.Expired;
            lot.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var lostValue = overdue.Sum(l => (int)Math.Round(l.ReceivedQuantity * l.UnitCost));
        _logger.LogWarning(
            "Đã đánh dấu {Count} lô hết hạn, tổn thất khoảng {Value}đ", overdue.Count, lostValue);

        return overdue.Count;
    }

    // ==========================================================================
    //  TIỆN ÍCH
    // ==========================================================================

    /// <summary>
    /// Làm tròn 4 chữ số thập phân sau MỖI phép cộng trừ số lượng.
    /// Không làm việc này thì sai số dấu phẩy động tích lũy và tồn kho sẽ
    /// hiện những giá trị như 0,00000000001 thay vì 0.
    /// </summary>
    private static double Round(double value) => Math.Round(value, 4);

    /// <summary>Mã lô tự sinh: [SKU]-[YYMMDD]-[4 ký tự ngẫu nhiên].</summary>
    private static string GenerateLotCode(string sku)
        => $"{sku}-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
}
