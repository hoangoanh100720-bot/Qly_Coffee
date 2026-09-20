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
    private readonly IInventoryService _inventory;
    private readonly IPrepService _prep;

    /// <summary>
    /// Ngưỡng "nấu nhanh được", tính bằng phút.
    /// <para>
    /// Dưới ngưỡng thì quầy báo khách chờ và cho bấm nấu ngay. Trên ngưỡng thì
    /// món chuyển sang "Tạm ngưng" — bắt khách đứng chờ 90 phút ủ một mẻ cốt trà
    /// không phải là phục vụ, đó là làm mất khách.
    /// </para>
    /// <para>
    /// 20 phút là mức khách Việt còn chấp nhận đứng chờ một ly nước. Chỉnh bằng
    /// biến POS_PREP_QUICK_MINUTES trong .env nếu quán thấy khác.
    /// </para>
    /// </summary>
    private static int QuickPrepMinutes =>
        int.TryParse(Environment.GetEnvironmentVariable("POS_PREP_QUICK_MINUTES"), out var v) ? v : 20;

    public PosController(
        AppDbContext db, IOrderService orders, IBarQueueService queue,
        IInventoryService inventory, IPrepService prep)
    {
        _db = db;
        _orders = orders;
        _queue = queue;
        _inventory = inventory;
        _prep = prep;
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
                    .ToList(),

                // CHỈ nhóm tùy chọn KHÔNG bắt buộc, tức là topping.
                //
                // Mức đường và mức đá cũng là nhóm tùy chọn nhưng IsRequired = true,
                // giá bằng 0, và ở quầy đã có nút bấm nhanh ("Ít đá", "Không ngọt")
                // xử lý rồi. Kéo chúng vào đây nữa thì nhân viên phải chọn hai lần
                // cho cùng một thứ, mà quầy là nơi mỗi cú chạm thừa đều làm khách chờ.
                ToppingIds = p.ModifierLinks
                    .Where(l => l.Group != null && !l.Group.IsRequired)
                    .OrderBy(l => l.SortOrder)
                    .SelectMany(l => l.Group!.Modifiers
                        .Where(m => m.IsActive)
                        .OrderBy(m => m.SortOrder)
                        .Select(m => m.Id))
                    .ToList()
            })
            .ToListAsync(ct);

        // ---- Bảng topping dùng chung ----------------------------------------
        // Nạp một lần rồi để frontend tra theo id. Xem chú thích ở PosProductDto
        // để biết vì sao không nhúng thẳng vào từng món.
        var toppingIds = products.SelectMany(p => p.ToppingIds).Distinct().ToList();

        var toppingRows = await _db.Modifiers
            .AsNoTracking()
            .Where(m => toppingIds.Contains(m.Id))
            .OrderBy(m => m.SortOrder)
            .Select(m => new
            {
                m.Id, m.Name, m.PriceDelta, m.ColorHex,
                Recipe = m.RecipeItems.Select(r => new { r.IngredientId, r.Quantity }).ToList()
            })
            .ToListAsync(ct);

        // ---- Topping nào còn nguyên liệu ------------------------------------
        //
        //  BẮT BUỘC phải kiểm, không được để mặc định "còn": trân châu đen nấu
        //  xong chỉ để được 3 ngày nên hết hàng giữa buổi là chuyện thường. Nếu
        //  vẫn cho bấm, đơn nhận được nhưng tới lúc bấm HOÀN TẤT mới báo thiếu
        //  nguyên liệu — tức là báo sau khi khách đã trả tiền và đứng chờ.
        //
        //  Tồn kho tra một lần cho mỗi nguyên liệu rồi dùng lại: bảy topping
        //  thường chỉ đụng bảy nguyên liệu, nhưng kem cheese vừa là topping vừa
        //  nằm trong công thức vài món nên vẫn có trùng.
        var stockCache = new Dictionary<Guid, double>();

        async Task<double> StockOf(Guid ingredientId)
        {
            if (stockCache.TryGetValue(ingredientId, out var cached)) return cached;
            var s = await _inventory.GetAvailableStockAsync(ingredientId, ct);
            stockCache[ingredientId] = s;
            return s;
        }

        // ---- Nguyên liệu nào NẤU THÊM ĐƯỢC ngay bây giờ ---------------------
        //
        //  Tra một lần cho cả màn hình. Mỗi công thức sơ chế làm ra đúng một
        //  bán thành phẩm, nên tra ngược theo OutputIngredientId là đủ.
        //
        //  MaxBatches < 1 nghĩa là hết cả nguyên liệu THÔ — nấu cũng không nấu
        //  được, đó là lúc phải nhập hàng chứ không phải lúc bảo khách chờ.
        var prepStatuses = await _prep.GetStatusAsync(CurrentStoreId, ct);

        var prepByOutput = prepStatuses
            .Where(s => s.MaxBatches >= 1)
            .GroupBy(s => s.Recipe.OutputIngredientId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Recipe.PrepMinutes).First());

        var toppings = new List<PosToppingDto>();

        foreach (var m in toppingRows)
        {
            // Topping không có công thức thì không trừ kho được — coi như luôn còn.
            // Chặn ở đây là chặn nhầm: lỗi nằm ở chỗ thiếu công thức, không phải hết hàng.
            var available = true;
            Guid? thieuId = null;

            foreach (var line in m.Recipe)
            {
                if (await StockOf(line.IngredientId) < line.Quantity)
                {
                    available = false;
                    thieuId = line.IngredientId;
                    break;
                }
            }

            // Hết thì tra xem có nấu thêm được không, và mất bao lâu.
            PrepStatus? cachNau = null;
            if (!available && thieuId is Guid tid) prepByOutput.TryGetValue(tid, out cachNau);

            toppings.Add(new PosToppingDto(
                m.Id, m.Name, m.PriceDelta, m.ColorHex, available,
                PrepRecipeId: cachNau?.Recipe.Id,
                PrepName:     cachNau?.Recipe.Name,
                PrepMinutes:  cachNau?.Recipe.PrepMinutes ?? 0));
        }

        // ---- Món hết hàng: nấu thêm được hay phải ngưng bán? ------------------
        //
        //  Chỉ tính cho món ĐANG HẾT — món còn bán được thì không cần tra gì.
        //  Dòng công thức TÙY CHỌN (đá, đường) không tính: hết đá không phải lý
        //  do ngưng bán cà phê.
        var hetHang = products.Where(p => !p.IsAvailable).Select(p => p.Id).ToList();

        var congThucMonHet = await _db.RecipeItems.AsNoTracking()
            .Where(r => hetHang.Contains(r.ProductId) && !r.IsOptional)
            .Select(r => new { r.ProductId, r.IngredientId, r.Quantity })
            .ToListAsync(ct);

        var prepTheoMon = new Dictionary<Guid, (int Phut, Guid? RecipeId, bool Ngung)>();

        foreach (var nhom in congThucMonHet.GroupBy(x => x.ProductId))
        {
            var thieu = new List<Guid>();
            foreach (var dong in nhom)
                if (await StockOf(dong.IngredientId) < dong.Quantity) thieu.Add(dong.IngredientId);

            // Không thiếu nguyên liệu nào → món hết vì hàng chờ đã chiếm hết chỗ,
            // chờ pha xong là bán tiếp. Không phải chuyện của sơ chế.
            if (thieu.Count == 0) continue;

            var cachNau = thieu.Select(i => prepByOutput.TryGetValue(i, out var s) ? s : null).ToList();

            // Chỉ báo "chờ N phút" khi MỌI thứ đang thiếu đều nấu được. Thiếu một
            // thứ phải đi mua thì nấu mấy mẻ cũng không bán được món này.
            if (cachNau.Any(s => s is null))
            {
                prepTheoMon[nhom.Key] = (0, null, true);
                continue;
            }

            var phut = cachNau.Max(s => s!.Recipe.PrepMinutes);
            var lauNhat = cachNau.OrderByDescending(s => s!.Recipe.PrepMinutes).First()!.Recipe.Id;

            // Nấu được thì LUÔN trả về công thức, kể cả khi quá lâu: nút "+" ở quầy
            // vẫn phải bấm được. Chỉ có LỜI HỨA VỚI KHÁCH là khác nhau — dưới ngưỡng
            // thì báo "chờ N phút", trên ngưỡng thì "tạm ngưng" và nhân viên tự
            // quyết định có nấu để bán ca sau hay không.
            prepTheoMon[nhom.Key] = phut <= QuickPrepMinutes
                ? (phut, lauNhat, false)
                : (phut, lauNhat, true);
        }

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
                Variants:        p.Variants,
                ToppingIds:      p.ToppingIds,
                PrepWaitMinutes: prepTheoMon.TryGetValue(p.Id, out var pr) && !pr.Ngung ? pr.Phut : 0,
                PrepRecipeId:    prepTheoMon.TryGetValue(p.Id, out var pr2) ? pr2.RecipeId : null,
                IsSuspended:     prepTheoMon.TryGetValue(p.Id, out var pr3) && pr3.Ngung);
        }).ToList();

        return Ok(new PosMenuDto(categories, result, toppings));
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
            // Trả ĐỦ danh sách nguyên liệu thiếu để máy quầy hiện một lần
            return Fail<object>(409, "INSUFFICIENT_STOCK", ex.Message,
                new StockShortageDetailsDto(ex.Shortages, ex.Ingredients));
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
    private readonly IRecipeService _recipe;

    public BarController(IBarQueueService queue, IOrderService orders, IRecipeService recipe)
    {
        _queue = queue;
        _orders = orders;
        _recipe = recipe;
    }

    /// <summary>
    /// Phiếu pha của một món trong đơn: định lượng cho một ly theo đúng size,
    /// mức đá, mức đường và topping khách chọn. Người pha bấm "Công thức" trên
    /// thẻ món là thấy, khỏi phải nhớ hay mở trang quản lý món.
    /// </summary>
    [HttpGet("items/{orderItemId:guid}/recipe")]
    public async Task<IActionResult> GetItemRecipe(Guid orderItemId, CancellationToken ct)
    {
        var card = await _recipe.GetBrewCardAsync(orderItemId, ct);
        return card is null
            ? Fail<BarRecipeDto>(404, "NOT_FOUND", "Không tìm thấy món này trong đơn.")
            : Ok(card);
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
        catch (IngredientShortageException ex)
        {
            return Fail<object>(409, "INSUFFICIENT_STOCK", ex.Message,
                new StockShortageDetailsDto(Array.Empty<StockShortageDto>(), ex.Shortages));
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
