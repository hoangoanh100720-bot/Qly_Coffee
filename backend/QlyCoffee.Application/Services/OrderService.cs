using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QlyCoffee.Domain.Entities;
using QlyCoffee.Domain.Enums;
using QlyCoffee.Infrastructure.Persistence;
using QlyCoffee.Shared;

namespace QlyCoffee.Application.Services;

// ==============================================================================
//  DỊCH VỤ ĐƠN HÀNG
//
//  VÒNG ĐỜI VÀ TÁC ĐỘNG LÊN KHO:
//
//     Pending    ─┐  đơn online chờ nhân viên xác nhận. Chưa trừ kho.
//                 │
//     Confirmed  ─┤  vào hàng pha, hẹn giờ với khách. Chưa trừ kho.
//     Preparing  ─┤  đang pha.                        Chưa trừ kho.
//     Ready      ─┤  pha xong, chờ khách nhận.        Chưa trừ kho.
//                 │
//     Completed  ─┘  ⭐ TRỪ KHO TẠI ĐÂY (một transaction Serializable)
//
//     Cancelled  ───  HOÀN KHO, nhưng chỉ khi đơn đã Completed rồi mới hủy.
//                     Hủy lúc đang pha thì không phải hoàn gì — chưa trừ bao giờ.
//
//  TẠI SAO TRỪ Ở COMPLETED CHỨ KHÔNG PHẢI CONFIRMED:
//  Nguyên liệu chỉ thật sự rời khỏi kho khi nhân viên thật sự pha ra ly nước.
//  Trừ sớm ở Confirmed thì mỗi đơn hủy giữa chừng đều phải hoàn kho, mà hoàn kho
//  là đường dễ sai nhất (hoàn nhầm lô, hoàn hai lần, hoàn sau khi lô đã hết hạn).
//
//  CÁI GIÁ PHẢI TRẢ, VÀ CÁCH TRẢ:
//  Từ lúc nhận đơn tới lúc pha xong, kho vẫn báo đủ dù mấy ly trong hàng chờ đã
//  chiếm phần. Giờ cao điểm sẽ nhận vượt. Nên EnsureIngredientsAvailableAsync
//  lấy tồn kho TRỪ ĐI phần các đơn đang trong hàng pha đã xí trước.
//
//  Không có trạng thái nào khác chạm vào kho. Nếu bạn thấy mình định thêm
//  logic trừ kho ở chỗ khác trong file này thì gần như chắc chắn là sai.
// ==============================================================================

public interface IOrderService
{
    Task<Order> CreateOrderAsync(CreateOrderRequest req, Guid storeId, Guid? userId, CancellationToken ct = default);
    Task<Order> ConfirmOrderAsync(Guid orderId, Guid actorId, CancellationToken ct = default);
    Task<Order> UpdateStatusAsync(Guid orderId, OrderStatus status, Guid actorId, string? reason, CancellationToken ct = default);
    Task<Order> CancelOrderAsync(Guid orderId, Guid actorId, string reason, CancellationToken ct = default);

    /// <summary>Nhân viên bấm đơn tại quầy — vào thẳng hàng pha, không chờ xác nhận.</summary>
    Task<Order> CreateInStoreOrderAsync(CreateOrderRequest req, Guid storeId, Guid actorId, CancellationToken ct = default);

    /// <summary>Pha xong, giao khách — ĐÂY là lúc trừ kho.</summary>
    Task<Order> CompleteOrderAsync(Guid orderId, Guid actorId, CancellationToken ct = default);
}

public class OrderService : IOrderService
{
    private readonly AppDbContext _db;
    private readonly IRecipeService _recipe;
    private readonly IInventoryService _inventory;
    private readonly IAvailabilityService _availability;
    private readonly IBarQueueService _queue;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        AppDbContext db,
        IRecipeService recipe,
        IInventoryService inventory,
        IAvailabilityService availability,
        IBarQueueService queue,
        ILogger<OrderService> logger)
    {
        _db = db;
        _recipe = recipe;
        _inventory = inventory;
        _availability = availability;
        _queue = queue;
        _logger = logger;
    }

    // ==========================================================================
    //  TẠO ĐƠN — CHƯA TRỪ KHO
    // ==========================================================================

    public async Task<Order> CreateOrderAsync(
        CreateOrderRequest req, Guid storeId, Guid? userId, CancellationToken ct = default)
    {
        if (req.Items.Count == 0)
            throw new BusinessRuleException("Đơn hàng phải có ít nhất một món");

        // ---- Kiểm tra khả dụng TRƯỚC khi tạo đơn ---------------------------
        // Kiểm tra ở đây là để báo lỗi thân thiện cho khách. Kiểm tra THẬT
        // diễn ra lúc xác nhận, bên trong transaction có khóa hàng.
        var shortages = new List<StockShortageDto>();
        foreach (var item in req.Items)
        {
            var (maxServings, blocking) = await _availability
                .ComputeMaxServingsAsync(item.ProductId, item.VariantId, ct);

            if (maxServings < item.Quantity)
            {
                var product = await _db.Products.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == item.ProductId, ct);
                shortages.Add(new StockShortageDto(
                    item.ProductId,
                    product?.Name ?? "Món không xác định",
                    maxServings,
                    // blocking đã là câu hoàn chỉnh — xem IAvailabilityService.
                    maxServings == 0 ? blocking ?? "Tạm hết" : $"Chỉ còn {maxServings} ly"));
            }
        }

        if (shortages.Count > 0)
            throw new StockShortageException(shortages);

        // ---- Dựng đơn -------------------------------------------------------
        var order = new Order
        {
            StoreId       = storeId,
            Code          = await GenerateOrderCodeAsync(storeId, ct),
            UserId        = userId,
            CustomerName  = req.CustomerName.Trim(),
            CustomerPhone = NormalizePhone(req.CustomerPhone),
            OrderType     = (OrderType)req.OrderType,
            PaymentMethod = (PaymentMethod)req.PaymentMethod,
            Note          = req.Note,
            Status        = OrderStatus.Pending,
            PlacedAt      = DateTime.UtcNow
        };

        // Đơn chuyển khoản phải có mã tham chiếu, đó là khóa duy nhất để webhook
        // SePay tìm ngược ra đơn khi tiền về. Đơn tiền mặt để null cho sạch.
        if (order.PaymentMethod == PaymentMethod.BankTransfer)
            order.PaymentRef = await ResolvePaymentRefAsync(req.PaymentRef, ct);

        var subtotal = 0;

        foreach (var reqItem in req.Items)
        {
            var product = await _db.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == reqItem.ProductId && p.IsActive, ct)
                ?? throw new BusinessRuleException("Món không tồn tại hoặc đã ngừng bán");

            ProductVariant? variant = null;
            if (reqItem.VariantId is Guid vId)
                variant = await _db.ProductVariants.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == vId, ct);

            var modifiers = reqItem.ModifierIds.Count > 0
                ? await _db.Modifiers.AsNoTracking()
                    .Where(m => reqItem.ModifierIds.Contains(m.Id))
                    .ToListAsync(ct)
                : new List<Modifier>();

            // ⚠️ GIÁ LUÔN TÍNH Ở BACKEND, không bao giờ tin số tiền client gửi lên
            var unitPrice = product.BasePrice
                          + (variant?.PriceDelta ?? 0)
                          + modifiers.Sum(m => m.PriceDelta);

            // Áp khuyến mãi đang chạy cho món này
            var discount = await GetActiveDiscountAsync(product.Id, storeId, ct);
            if (discount > 0)
                unitPrice = (int)Math.Round(unitPrice * (100 - discount) / 100.0);

            var lineTotal = unitPrice * reqItem.Quantity;
            subtotal += lineTotal;

            order.Items.Add(new OrderItem
            {
                ProductId     = product.Id,
                VariantId     = variant?.Id,
                // Chụp lại tên và giá — đơn cũ phải giữ nguyên dù sau này quán đổi giá
                ProductName   = product.Name,
                VariantName   = variant?.Name,
                Quantity      = reqItem.Quantity,
                UnitPrice     = unitPrice,
                LineTotal     = lineTotal,
                ModifiersJson = JsonSerializer.Serialize(modifiers.Select(m => new
                {
                    modifierId = m.Id,
                    name       = m.Name,
                    priceDelta = m.PriceDelta,
                    colorHex   = m.ColorHex
                })),
                Note = reqItem.Note
            });
        }

        order.Subtotal = subtotal;

        // ---- Thuế GTGT ------------------------------------------------------
        //
        //  Cấu hình thuế lấy từ cửa hàng NHƯNG được CHỤP LẠI vào đơn. Thuế suất
        //  thay đổi theo nghị quyết của Quốc hội — hóa đơn in lại sau một năm
        //  phải ra đúng con số đã giao cho khách hôm đó.
        //
        //  Với chế độ mặc định (giá niêm yết đã gồm thuế, Luật Giá 2023 Điều 29)
        //  GrandTotal KHÔNG đổi: thuế chỉ được tách ngược ra khỏi tổng, không
        //  cộng thêm. Đây là lý do phép tính nằm ở QlyCoffee.Shared để giỏ hàng
        //  bên frontend hiện đúng từng đồng con số này.
        var storeTax = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => new { s.TaxMode, s.VatRatePercent })
            .FirstOrDefaultAsync(ct);

        var payable = subtotal - order.DiscountTotal;
        var vat = VatPolicy.Compute(
            payable,
            storeTax?.TaxMode ?? VatPolicy.DefaultMode,
            storeTax?.VatRatePercent ?? VatPolicy.DefaultRatePercent);

        order.TaxMode        = vat.Mode;
        order.TaxRatePercent = vat.RatePercent;
        order.NetAmount      = vat.Net;
        order.TaxAmount      = vat.Tax;
        order.GrandTotal     = vat.Gross;

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Tạo đơn {Code}, {Count} món, {Total}đ",
            order.Code, order.Items.Count, order.GrandTotal);

        return order;
    }

    // ==========================================================================
    //  XÁC NHẬN ĐƠN — ĐƯA VÀO HÀNG PHA
    // ==========================================================================

    /// <summary>
    /// Xác nhận đơn online: đưa vào hàng pha và chốt giờ hẹn với khách.
    /// <para>
    /// KHÔNG trừ kho ở đây — việc đó để tới lúc bấm Hoàn tất. Nhưng có kiểm tra
    /// nguyên liệu ngay, vì nhận một đơn biết chắc không pha nổi rồi mười phút
    /// sau mới báo hết hàng là cách nhanh nhất để mất khách.
    /// </para>
    /// </summary>
    public async Task<Order> ConfirmOrderAsync(
        Guid orderId, Guid actorId, CancellationToken ct = default)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy đơn hàng");

        if (order.Status != OrderStatus.Pending)
            throw new BusinessRuleException(
                $"Đơn {order.Code} đã được xử lý trước đó (trạng thái: {StatusLabel(order.Status)})");

        // Kiểm tra nguyên liệu NGAY BÂY GIỜ dù chưa trừ. Nhận một đơn mà biết
        // chắc không pha nổi là cách nhanh nhất để mất khách: nó nằm trong hàng
        // chờ mười phút rồi mới báo hết hàng.
        await EnsureIngredientsAvailableAsync(order, ct);

        var eta = await _queue.EstimateAsync(
            order.StoreId,
            order.Items.Select(i => (i.ProductId, i.Quantity)).ToList(), ct);

        order.Status           = OrderStatus.Confirmed;
        order.ConfirmedAt      = DateTime.UtcNow;
        order.EstimatedReadyAt = eta.ReadyAtUtc;
        order.UpdatedAt        = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Đơn {Code} vào hàng pha, hẹn {Minutes} phút ({Ahead} ly đang chờ trước)",
            order.Code, eta.Minutes, eta.QueuedItemsAhead);

        return order;
    }

    // ==========================================================================
    //  ĐẶT ĐƠN TẠI QUẦY
    // ==========================================================================

    /// <summary>
    /// Nhân viên bấm đơn cho khách đứng trước mặt.
    /// <para>
    /// Khác đơn online ở chỗ KHÔNG có bước chờ xác nhận: nhân viên vừa nhận tiền
    /// vừa bấm máy, nên đơn vào thẳng hàng pha. Bắt bấm "xác nhận" thêm một lần
    /// nữa chỉ là thao tác thừa ngay trước mặt khách.
    /// </para>
    /// </summary>
    public async Task<Order> CreateInStoreOrderAsync(
        CreateOrderRequest req, Guid storeId, Guid actorId, CancellationToken ct = default)
    {
        var order = await CreateOrderAsync(req, storeId, actorId, ct);

        order.Channel = OrderChannel.InStore;

        var eta = await _queue.EstimateAsync(
            storeId, order.Items.Select(i => (i.ProductId, i.Quantity)).ToList(), ct);

        order.Status           = OrderStatus.Confirmed;
        order.ConfirmedAt      = DateTime.UtcNow;
        order.EstimatedReadyAt = eta.ReadyAtUtc;
        order.UpdatedAt        = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Đơn quầy {Code}: {Cups} ly, {Total}đ, hẹn {Minutes} phút",
            order.Code, order.Items.Sum(i => i.Quantity), order.GrandTotal, eta.Minutes);

        return order;
    }

    // ==========================================================================
    //  HOÀN TẤT ĐƠN — TRỪ KHO
    // ==========================================================================

    /// <summary>
    /// Pha xong, giao khách. ĐÂY LÀ CHỖ DUY NHẤT TRỪ KHO.
    /// <para>
    /// Trừ vào lúc này chứ không phải lúc xác nhận, vì chỉ khi món thật sự được
    /// làm ra thì nguyên liệu mới thật sự rời khỏi kho. Đơn hủy giữa chừng vì thế
    /// không cần hoàn kho — nó chưa bao giờ bị trừ.
    /// </para>
    /// <para>
    /// Toàn bộ nằm trong MỘT transaction Serializable. Thiếu bất kỳ nguyên liệu
    /// nào thì hoàn tác sạch, đơn vẫn nằm trong hàng pha — không có chuyện trừ
    /// được nửa chừng rồi dừng.
    /// </para>
    /// </summary>
    public async Task<Order> CompleteOrderAsync(
        Guid orderId, Guid actorId, CancellationToken ct = default)
    {
        // Serializable là mức cô lập cao nhất. Chậm hơn ReadCommitted một chút
        // nhưng đây là chỗ duy nhất trong hệ thống thực sự cần nó.
        await using var transaction = await _db.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        try
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy đơn hàng");

            // ---- Ba chốt chặn chống xử lý trùng -----------------------------
            if (order.Status == OrderStatus.Completed)
                throw new BusinessRuleException($"Đơn {order.Code} đã hoàn tất trước đó rồi");

            if (order.Status == OrderStatus.Cancelled)
                throw new BusinessRuleException($"Đơn {order.Code} đã bị hủy, không hoàn tất được");

            if (order.StockDeducted)
                throw new BusinessRuleException($"Đơn {order.Code} đã trừ kho rồi");

            // ---- Gộp nhu cầu nguyên liệu của CẢ BILL -------------------------
            // Gộp trước rồi mới trừ, chứ không trừ từng món một. Hai món cùng
            // dùng sữa thì gộp lại thành một lần trừ — vừa ít bút toán hơn, vừa
            // tránh trường hợp trừ được món đầu rồi món sau mới báo thiếu.
            var requests = order.Items.Select(i => new ExplodeRequest(
                i.ProductId,
                i.VariantId,
                ParseModifierIds(i.ModifiersJson),
                i.Quantity));

            var requirements = await _recipe.AggregateOrderAsync(requests, ct);

            // ---- Trừ kho theo FEFO ------------------------------------------
            var costTotal = 0;
            foreach (var req in requirements)
            {
                var result = await _inventory.ConsumeFefoAsync(_db, new ConsumeRequest(
                    StoreId:        order.StoreId,
                    IngredientId:   req.IngredientId,
                    Quantity:       req.Quantity,
                    ReferenceType:  "ORDER",
                    ReferenceId:    order.Id,
                    // Khóa duy nhất theo đơn + nguyên liệu. Gửi lại yêu cầu cũng
                    // không trừ thêm lần nữa.
                    IdempotencyKey: $"ORDER:{order.Id}:ING:{req.IngredientId}",
                    ActorUserId:    actorId), ct);

                costTotal += result.TotalCost;
            }

            // ---- Cập nhật đơn ------------------------------------------------
            order.Status        = OrderStatus.Completed;
            order.CompletedAt   = DateTime.UtcNow;
            order.ReadyAt     ??= DateTime.UtcNow;
            order.StockDeducted = true;
            order.CostTotal     = costTotal;
            order.UpdatedAt     = DateTime.UtcNow;

            // Khách nhận hàng thì coi như đã trả tiền (tiền mặt tại quầy)
            if (order.PaymentStatus == PaymentStatus.Unpaid)
                order.PaymentStatus = PaymentStatus.Paid;

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Hoàn tất đơn {Code}, đã trừ kho, giá vốn {Cost}đ, lãi gộp {Profit}đ",
                order.Code, costTotal, order.GrandTotal - costTotal);

            // ---- Cập nhật khả dụng SAU transaction --------------------------
            // Để ngoài transaction vì đây là thao tác đọc nhiều, không cần
            // giữ khóa hàng lâu thêm.
            await _availability.RecomputeForIngredientsAsync(
                requirements.Select(r => r.IngredientId), ct);

            return order;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Kiểm tra nguyên liệu đủ cho cả đơn, KHÔNG trừ gì cả.
    /// <para>
    /// Vì kho chỉ bị trừ lúc hoàn tất, những ly đang nằm trong hàng pha vẫn chưa
    /// bị trừ. Nên phép kiểm tra phải lấy tồn kho TRỪ ĐI phần các đơn đang chờ đã
    /// chiếm chỗ — nếu không, giờ cao điểm sẽ nhận nhiều đơn hơn số ly pha nổi.
    /// </para>
    /// </summary>
    private async Task EnsureIngredientsAvailableAsync(Order order, CancellationToken ct)
    {
        var requirements = await _recipe.AggregateOrderAsync(
            order.Items.Select(i => new ExplodeRequest(
                i.ProductId, i.VariantId, ParseModifierIds(i.ModifiersJson), i.Quantity)), ct);

        // Phần nguyên liệu đã bị các đơn trong hàng pha "xí" trước
        var reserved = await GetReservedIngredientsAsync(order.StoreId, order.Id, ct);

        foreach (var req in requirements)
        {
            var onHand = await _db.InventoryLots
                .Where(l => l.StoreId == order.StoreId
                         && l.IngredientId == req.IngredientId
                         && l.Status == LotStatus.Active
                         && l.RemainingQuantity > 0)
                .SumAsync(l => (double)l.RemainingQuantity, ct);

            var taken = reserved.TryGetValue(req.IngredientId, out var r) ? r : 0;
            var usable = onHand - taken;

            if (usable < req.Quantity)
            {
                var name = await _db.Ingredients
                    .Where(i => i.Id == req.IngredientId)
                    .Select(i => i.Name)
                    .FirstOrDefaultAsync(ct) ?? "nguyên liệu";

                throw new InsufficientStockException(
                    req.IngredientId, name, (double)req.Quantity, (double)Math.Max(0, usable));
            }
        }
    }

    /// <summary>
    /// Lượng nguyên liệu mà các đơn ĐANG TRONG HÀNG PHA sẽ tiêu thụ khi hoàn tất.
    /// Bỏ qua chính đơn đang xét để nó không tự trừ phần của mình hai lần.
    /// </summary>
    private async Task<Dictionary<Guid, double>> GetReservedIngredientsAsync(
        Guid storeId, Guid excludeOrderId, CancellationToken ct)
    {
        var queued = await _db.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.StoreId == storeId
                     && i.OrderId != excludeOrderId
                     && !i.Order.StockDeducted
                     && (i.Order.Status == OrderStatus.Confirmed
                      || i.Order.Status == OrderStatus.Preparing
                      || i.Order.Status == OrderStatus.Ready))
            .Select(i => new { i.ProductId, i.VariantId, i.ModifiersJson, i.Quantity })
            .ToListAsync(ct);

        if (queued.Count == 0) return new Dictionary<Guid, double>();

        var reqs = await _recipe.AggregateOrderAsync(
            queued.Select(q => new ExplodeRequest(
                q.ProductId, q.VariantId, ParseModifierIds(q.ModifiersJson), q.Quantity)), ct);

        return reqs.ToDictionary(r => r.IngredientId, r => r.Quantity);
    }

    // ==========================================================================
    //  ĐỔI TRẠNG THÁI
    // ==========================================================================

    public async Task<Order> UpdateStatusAsync(
        Guid orderId, OrderStatus status, Guid actorId, string? reason, CancellationToken ct = default)
    {
        // Ba trạng thái có xử lý riêng — chuyển sang hàm chuyên trách.
        // Completed nằm ở đây vì nó là chỗ TRỪ KHO, cần transaction Serializable.
        if (status == OrderStatus.Confirmed) return await ConfirmOrderAsync(orderId, actorId, ct);
        if (status == OrderStatus.Completed) return await CompleteOrderAsync(orderId, actorId, ct);
        if (status == OrderStatus.Cancelled)
            return await CancelOrderAsync(orderId, actorId, reason ?? "Không rõ lý do", ct);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy đơn hàng");

        if (!IsValidTransition(order.Status, status))
            throw new BusinessRuleException(
                $"Không thể chuyển từ {StatusLabel(order.Status)} sang {StatusLabel(status)}");

        order.Status = status;
        order.UpdatedAt = DateTime.UtcNow;

        if (status == OrderStatus.Ready) order.ReadyAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return order;
    }

    // ==========================================================================
    //  HỦY ĐƠN — HOÀN KHO
    // ==========================================================================

    public async Task<Order> CancelOrderAsync(
        Guid orderId, Guid actorId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleException("Phải nhập lý do hủy đơn");

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy đơn hàng");

            if (order.Status == OrderStatus.Cancelled)
                throw new BusinessRuleException("Đơn đã bị hủy trước đó");

            if (order.Status == OrderStatus.Completed)
                throw new BusinessRuleException(
                    "Không thể hủy đơn đã giao cho khách. Dùng chức năng hoàn tiền nếu cần.");

            // ---- Hoàn kho nếu trước đó đã trừ ------------------------------
            if (order.StockDeducted && !order.StockReturned)
            {
                await _inventory.ReturnStockForOrderAsync(_db, order.Id, actorId, ct);
                order.StockReturned = true;
            }

            order.Status       = OrderStatus.Cancelled;
            order.CancelledAt  = DateTime.UtcNow;
            order.CancelReason = reason;
            order.UpdatedAt    = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation("Hủy đơn {Code}, lý do: {Reason}, đã hoàn kho: {Returned}",
                order.Code, reason, order.StockReturned);

            // Hoàn kho xong thì món có thể bán lại được
            var ingredientIds = await _db.StockMovements
                .Where(m => m.ReferenceType == "ORDER" && m.ReferenceId == order.Id)
                .Select(m => m.IngredientId)
                .Distinct()
                .ToListAsync(ct);

            await _availability.RecomputeForIngredientsAsync(ingredientIds, ct);

            return order;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    // ==========================================================================
    //  TIỆN ÍCH
    // ==========================================================================

    /// <summary>
    /// Bảng chuyển trạng thái hợp lệ. Ngăn các bước nhảy vô lý như
    /// từ Pending thẳng sang Completed mà chưa trừ kho.
    /// </summary>
    private static bool IsValidTransition(OrderStatus from, OrderStatus to) => (from, to) switch
    {
        (OrderStatus.Pending,   OrderStatus.Confirmed) => true,
        (OrderStatus.Confirmed, OrderStatus.Preparing) => true,
        (OrderStatus.Preparing, OrderStatus.Ready)     => true,
        (OrderStatus.Ready,     OrderStatus.Completed) => true,
        // Cho phép bỏ qua bước pha chế khi quán vắng
        (OrderStatus.Confirmed, OrderStatus.Ready)     => true,
        (OrderStatus.Confirmed, OrderStatus.Completed) => true,
        (_, OrderStatus.Cancelled)                     => true,
        _ => false
    };

    public static string StatusLabel(OrderStatus s) => s switch
    {
        OrderStatus.Pending   => "Chờ xác nhận",
        OrderStatus.Confirmed => "Đã xác nhận",
        OrderStatus.Preparing => "Đang pha chế",
        OrderStatus.Ready     => "Đã xong",
        OrderStatus.Completed => "Đã giao",
        OrderStatus.Cancelled => "Đã hủy",
        _ => s.ToString()
    };

    /// <summary>
    /// Mã đơn theo ngày: QC-YYMMDD-NNNN.
    /// Đếm theo ngày kinh doanh giờ Việt Nam, không phải UTC.
    /// </summary>
    private async Task<string> GenerateOrderCodeAsync(Guid storeId, CancellationToken ct)
    {
        var vnNow = VietnamTime.Now();
        var dayStart = VietnamTime.ToUtc(vnNow.Date);
        var dayEnd = dayStart.AddDays(1);

        var count = await _db.Orders
            .IgnoreQueryFilters()
            .CountAsync(o => o.StoreId == storeId
                          && o.PlacedAt >= dayStart
                          && o.PlacedAt < dayEnd, ct);

        return $"QC-{vnNow:yyMMdd}-{count + 1:D4}";
    }

    // ==========================================================================
    //  MÃ THAM CHIẾU CHUYỂN KHOẢN
    // ==========================================================================

    /// <summary>
    /// Bảng chữ cái của mã tham chiếu. Trùng khớp với BankQr.NewReference() bên
    /// frontend — hai nơi lệch nhau thì mã máy quầy in ra QR sẽ bị backend coi là
    /// sai định dạng và thay bằng mã khác, khách quét một mã mà đơn lưu mã khác.
    ///
    /// Cố tình bỏ 0/O, 1/I/L, 5/S, 8/B: nhân viên phải đọc mã này để đối chiếu
    /// với thông báo ngân hàng, mà đó là những cặp hay đọc nhầm nhất.
    /// </summary>
    private const string RefAlphabet = "ACDEFGHJKMNPQRTUVWXY2346789";

    /// <summary>Mã tham chiếu hợp lệ: MCC + đúng 5 ký tự trong bảng trên.</summary>
    private static readonly Regex RefPattern =
        new($"^MCC[{RefAlphabet}]{{5}}$", RegexOptions.Compiled);

    /// <summary>
    /// Chốt mã tham chiếu cho một đơn chuyển khoản.
    ///
    /// Ưu tiên dùng đúng mã máy quầy đã in lên QR — khách quét mã nào thì đơn
    /// phải mang mã đó, nếu không webhook không khớp được. Chỉ bỏ mã của client
    /// trong hai trường hợp thật sự không dùng được: sai định dạng (dữ liệu do
    /// bên ngoài gửi, không bao giờ tin sẵn) hoặc đã có đơn khác giữ mã đó
    /// (cột payment_ref có chỉ mục duy nhất, để nguyên là vỡ lúc lưu).
    /// </summary>
    private async Task<string> ResolvePaymentRefAsync(string? requested, CancellationToken ct)
    {
        var wanted = requested?.Trim().ToUpperInvariant();

        if (!string.IsNullOrEmpty(wanted) && RefPattern.IsMatch(wanted))
        {
            if (!await RefTakenAsync(wanted, ct)) return wanted;

            _logger.LogWarning(
                "Mã tham chiếu {Ref} máy quầy gửi lên đã có đơn khác dùng — cấp mã mới. "
              + "Khách có thể đã quét mã cũ, cần đối soát tay giao dịch này.", wanted);
        }

        // Tự sinh. Thử vài lần rồi thôi: không gian mã là 27^5 ≈ 14,3 triệu nên
        // đụng nhau đã hiếm, đụng liên tiếp năm lần thì gần như chắc chắn là lỗi
        // ở chỗ khác chứ không phải xui.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = NewRef();
            if (!await RefTakenAsync(candidate, ct)) return candidate;
        }

        throw new BusinessRuleException(
            "Không cấp được mã tham chiếu chuyển khoản. Thử lại hoặc thu tiền mặt.");
    }

    private async Task<bool> RefTakenAsync(string reference, CancellationToken ct)
        => await _db.Orders
            .IgnoreQueryFilters()
            .AnyAsync(o => o.PaymentRef == reference, ct);

    private static string NewRef()
        => "MCC" + new string(Enumerable
            .Range(0, 5)
            .Select(_ => RefAlphabet[Random.Shared.Next(RefAlphabet.Length)])
            .ToArray());

    /// <summary>Chuẩn hóa số điện thoại về dạng 0xxxxxxxxx.</summary>
    private static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("84") && digits.Length == 11) digits = "0" + digits[2..];
        return digits;
    }

    private static List<Guid> ParseModifierIds(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                .Select(e => e.GetProperty("modifierId").GetGuid())
                .ToList();
        }
        catch { return new List<Guid>(); }
    }

    /// <summary>Phần trăm giảm giá đang có hiệu lực cho món. 0 nếu không có.</summary>
    private async Task<int> GetActiveDiscountAsync(Guid productId, Guid storeId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var promo = await _db.Promotions
            .AsNoTracking()
            .Where(p => p.StoreId == storeId
                     && p.Status == PromotionStatus.Active
                     && p.StartsAt <= now && p.EndsAt >= now
                     && (p.ProductId == productId || p.ProductId == null)
                     && p.Type == PromotionType.PercentOff)
            // Khuyến mãi riêng cho món được ưu tiên hơn khuyến mãi toàn menu
            .OrderByDescending(p => p.ProductId != null)
            .ThenByDescending(p => p.Value)
            .FirstOrDefaultAsync(ct);

        return promo?.Value ?? 0;
    }
}

/// <summary>Ném ra khi món trong đơn không đủ nguyên liệu. Mang danh sách cụ thể để báo cho khách.</summary>
public class StockShortageException : Exception
{
    public IReadOnlyList<StockShortageDto> Shortages { get; }

    public StockShortageException(IReadOnlyList<StockShortageDto> shortages)
        : base("Một số món đã hết nguyên liệu")
        => Shortages = shortages;
}

/// <summary>
/// Tiện ích múi giờ Việt Nam.
/// <para>
/// MỌI phép tính "theo ngày kinh doanh" phải đi qua đây. Dùng thẳng
/// <c>DateTime.Today</c> sẽ sai vì server thường chạy giờ UTC — đơn lúc 23h
/// đêm giờ Việt Nam sẽ bị tính sang ngày hôm sau.
/// </para>
/// </summary>
public static class VietnamTime
{
    private static readonly TimeZoneInfo Tz = GetTimeZone();

    private static TimeZoneInfo GetTimeZone()
    {
        // Tên múi giờ khác nhau giữa Windows và Linux
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        // Dự phòng: Việt Nam là UTC+7 quanh năm, không có giờ mùa hè
        return TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "Vietnam", "Vietnam");
    }

    public static DateTime Now() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);

    public static DateTime ToUtc(DateTime vnTime)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(vnTime, DateTimeKind.Unspecified), Tz);

    /// <summary>
    /// Quy đổi một ngày lịch sang DateTime Kind=Utc lúc 0h.
    ///
    /// Cột expiry_date là timestamptz và được lưu theo quy ước "0h UTC của ngày
    /// hết hạn" (xem DatabaseSeeder). Npgsql TỪ CHỐI mọi DateTime Kind=Unspecified
    /// khi so sánh với timestamptz, mà VietnamTime.Now() lại trả về Unspecified —
    /// nên mọi mốc ngày đưa vào truy vấn đều phải đi qua hàm này.
    /// </summary>
    public static DateTime CalendarDayUtc(DateTime day)
        => DateTime.SpecifyKind(day.Date, DateTimeKind.Utc);

    /// <summary>Ngày kinh doanh dạng "yyyy-MM-dd" theo giờ Việt Nam.</summary>
    public static string BusinessDate() => Now().ToString("yyyy-MM-dd");

    public static string BusinessDate(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(utc, Tz).ToString("yyyy-MM-dd");
}
