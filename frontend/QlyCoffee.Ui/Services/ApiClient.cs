using System.Net.Http.Json;
using System.Text.Json;
using QlyCoffee.Shared;

namespace QlyCoffee.Client.Services;

/// <summary>
/// Cầu nối duy nhất giữa giao diện và backend.
/// <para>
/// Mọi trang đều gọi qua lớp này, không gọi <c>HttpClient</c> trực tiếp. Nhờ vậy:
/// (1) xử lý lỗi thống nhất — trang chỉ cần kiểm tra <c>Success</c>;
/// (2) đổi đường dẫn API chỉ sửa một chỗ;
/// (3) thêm token xác thực hay retry cũng chỉ sửa một chỗ.
/// </para>
/// <para>
/// Địa chỉ backend đọc từ <c>wwwroot/appsettings.json</c>, giá trị này được
/// sinh ra từ biến <c>CLIENT_API_URL</c> trong file <c>.env</c> lúc build.
/// </para>
/// </summary>
public class ApiClient
{
    private readonly HttpClient _http;
    private readonly AlertService _alerts;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(HttpClient http, AlertService alerts)
    {
        _http = http;
        _alerts = alerts;
    }

    // ==========================================================================
    //  CỬA HÀNG
    // ==========================================================================

    /// <summary>
    /// Thông tin quán, trong đó có cấu hình thuế GTGT mà giỏ hàng cần để hiện
    /// đúng số thuế TRƯỚC khi khách bấm đặt.
    /// </summary>
    public Task<ApiResponse<StoreInfoDto>> GetStoreInfoAsync()
        => GetAsync<StoreInfoDto>("api/shop/cua-hang");

    // ==========================================================================
    //  MENU — trang bán hàng
    // ==========================================================================

    public record MenuResponse(
        IReadOnlyList<CategoryDto> Categories,
        IReadOnlyList<ProductCardDto> Products);

    public Task<ApiResponse<MenuResponse>> GetMenuAsync()
        => GetAsync<MenuResponse>("api/menu");

    public Task<ApiResponse<ProductDetailDto>> GetProductAsync(string slug)
        => GetAsync<ProductDetailDto>($"api/menu/{slug}");

    /// <summary>
    /// Kiểm tra tồn kho theo thời gian thực cho giỏ hàng.
    /// Gọi khi vào trang giỏ, vì tồn kho có thể đã đổi từ lúc khách thêm món.
    /// </summary>
    public Task<ApiResponse<Dictionary<Guid, int>>> CheckAvailabilityAsync(IEnumerable<Guid> productIds)
        => PostAsync<Dictionary<Guid, int>>("api/menu/availability", new { ProductIds = productIds });

    // ==========================================================================
    //  ĐƠN HÀNG
    // ==========================================================================

    public Task<ApiResponse<CreateOrderResult>> CreateOrderAsync(CreateOrderRequest req)
        => PostAsync<CreateOrderResult>("api/orders", req);

    public Task<ApiResponse<OrderDto>> GetOrderByCodeAsync(string code)
        => GetAsync<OrderDto>($"api/orders/{code}");

    public Task<ApiResponse<PagedResult<OrderDto>>> GetOrdersAsync(int? status = null, int page = 1)
        => GetAsync<PagedResult<OrderDto>>($"api/admin/orders?status={status}&page={page}");

    /// <summary>
    /// Đổi trạng thái đơn. Chuyển sang <c>Confirmed</c> (1) sẽ TRỪ KHO ở backend —
    /// giao diện không tự tính toán tồn kho.
    /// </summary>
    public Task<ApiResponse<OrderDto>> UpdateOrderStatusAsync(Guid orderId, int status, string? reason = null)
        => PostAsync<OrderDto>($"api/orders/{orderId}/status", new { Status = status, Reason = reason });

    // ==========================================================================
    //  KHO
    // ==========================================================================

    public Task<ApiResponse<List<IngredientStockDto>>> GetIngredientsAsync()
        => GetAsync<List<IngredientStockDto>>("api/inventory/ingredients");

    public Task<ApiResponse<List<InventoryLotDto>>> GetInventoryLotsAsync()
        => GetAsync<List<InventoryLotDto>>("api/inventory/lots");

    /// <summary>
    /// Danh sách nguyên liệu cần nhập của một ngày ("yyyy-MM-dd"; null = hôm nay).
    /// Dùng chung cho trang Kế hoạch và trang Nhập kho.
    /// </summary>
    public Task<ApiResponse<RestockListDto>> GetRestockListAsync(string? businessDate = null)
        => GetAsync<RestockListDto>(string.IsNullOrEmpty(businessDate)
            ? "api/inventory/restock"
            : $"api/inventory/restock?date={Uri.EscapeDataString(businessDate)}");

    /// <summary>
    /// Rút danh sách nguyên liệu thiếu từ lỗi INSUFFICIENT_STOCK.
    /// <para>
    /// Trường <c>details</c> của lỗi được giải mã thành <c>object</c> (thực chất
    /// là JsonElement) vì mỗi loại lỗi mang một hình dạng khác. Hàm này đọc
    /// đúng hình dạng <see cref="StockShortageDetailsDto"/>; lỗi khác hoặc
    /// backend cũ thì trả danh sách rỗng, trang tự quay về hiện câu thông báo.
    /// </para>
    /// </summary>
    public static List<IngredientShortageDto> ReadIngredientShortages(ApiError? error)
    {
        if (error?.Code != "INSUFFICIENT_STOCK" || error.Details is not JsonElement el
            || el.ValueKind != JsonValueKind.Object)
            return new();

        try
        {
            return el.Deserialize<StockShortageDetailsDto>(JsonOpts)?.Ingredients.ToList() ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public Task<ApiResponse<InventoryLotDto>> ReceiveStockAsync(ReceiveStockRequest req)
        => PostAsync<InventoryLotDto>("api/inventory/receive", req);

    public Task<ApiResponse<bool>> RecordWasteAsync(RecordWasteRequest req)
        => PostAsync<bool>("api/inventory/waste", req);

    /// <summary>Tiêu hủy lô quá hạn và ghi bút toán hao hụt tương ứng.</summary>
    public Task<ApiResponse<bool>> DisposeLotAsync(Guid lotId)
        => PostAsync<bool>($"api/inventory/lots/{lotId}/dispose", new { });

    // ==========================================================================
    //  SƠ CHẾ — bước đứng TRƯỚC bán hàng
    // ==========================================================================

    /// <summary>
    /// Mọi công thức sơ chế kèm tình trạng: còn bao nhiêu cốt, mẻ nào sắp hết
    /// hạn, còn nguyên liệu thô để ủ mấy mẻ nữa. Một lần gọi là đủ vẽ cả trang.
    /// </summary>
    public Task<ApiResponse<List<PrepRecipeDto>>> GetPrepRecipesAsync()
        => GetAsync<List<PrepRecipeDto>>("api/prep/recipes");

    /// <summary>
    /// Chạy một mẻ sơ chế. Backend TRỪ NGUYÊN LIỆU THÔ tại đây và tạo một lô
    /// bán thành phẩm mới — giao diện không tự tính tồn kho.
    /// </summary>
    public Task<ApiResponse<PrepBatchResultDto>> ProduceBatchAsync(ProduceBatchRequest req)
        => PostAsync<PrepBatchResultDto>("api/prep/produce", req);

    // ==========================================================================
    //  MÓN & CÔNG THỨC
    // ==========================================================================

    public Task<ApiResponse<List<ProductAdminDto>>> GetAdminProductsAsync()
        => GetAsync<List<ProductAdminDto>>("api/admin/products");

    /// <summary>
    /// Nạp một lượt ba khối dữ liệu mà màn hình soạn công thức cần:
    /// thông tin món, các dòng công thức hiện có, và danh sách nguyên liệu để chọn.
    /// Gộp một lần gọi thay vì ba lần để trang không bị hiện từng phần.
    /// </summary>
    public async Task<(ProductAdminDto? Product, List<RecipeLineDto>? Lines, List<IngredientStockDto>? Ingredients, List<ProductToppingDto>? Toppings)>
        GetRecipeEditorDataAsync(Guid productId)
    {
        var res = await GetAsync<RecipeEditorData>($"api/admin/products/{productId}/recipe");
        return res.Success && res.Data is not null
            ? (res.Data.Product, res.Data.Lines, res.Data.Ingredients, res.Data.Toppings ?? new())
            : (null, null, null, null);
    }

    private record RecipeEditorData(
        ProductAdminDto Product,
        List<RecipeLineDto> Lines,
        List<IngredientStockDto> Ingredients,
        List<ProductToppingDto>? Toppings);

    /// <summary>
    /// Công thức của MỌI món trong một lần gọi, tra theo id món.
    /// Dùng khi mở sẵn công thức cả bảng — gọi từng món 56 lần thì bảng hiện dần
    /// từng dòng và backend phải chạy vài nghìn truy vấn tồn kho.
    /// </summary>
    public async Task<(Dictionary<Guid, List<RecipeLineDto>>? Lines, Dictionary<Guid, List<ProductToppingDto>>? Toppings)>
        GetAllRecipesAsync()
    {
        var res = await GetAsync<AllRecipesData>("api/admin/products/recipes");
        return res.Success && res.Data is not null
            ? (res.Data.Lines, res.Data.Toppings)
            : (null, null);
    }

    private record AllRecipesData(
        Dictionary<Guid, List<RecipeLineDto>> Lines,
        Dictionary<Guid, List<ProductToppingDto>> Toppings);

    public Task<ApiResponse<bool>> SaveRecipeAsync(Guid productId, object payload)
        => PutAsync<bool>($"api/admin/products/{productId}/recipe", payload);

    /// <summary>
    /// Đổi RIÊNG giá bán một món, không mở màn hình soạn công thức.
    /// Trả về bản DTO đã cập nhật để bảng vẽ lại biên lợi nhuận ngay.
    /// </summary>
    public Task<ApiResponse<ProductAdminDto>> UpdateProductPriceAsync(Guid productId, int basePrice)
        => PatchAsync<ProductAdminDto>($"api/admin/products/{productId}/price",
            new { BasePrice = basePrice });

    // ==========================================================================
    //  KẾ HOẠCH AI
    // ==========================================================================

    public Task<ApiResponse<DailyPlanDto>> GetDailyPlanAsync(string businessDate)
        => GetAsync<DailyPlanDto>($"api/plans/{businessDate}");

    public Task<ApiResponse<List<DailyPlanSummaryDto>>> GetPlanHistoryAsync()
        => GetAsync<List<DailyPlanSummaryDto>>("api/plans");

    /// <summary>
    /// Chạy ngay job phân tích thay vì chờ 23:00. Dùng để xem thử hoặc chạy lại
    /// khi job đêm bị lỗi.
    /// </summary>
    public Task<ApiResponse<DailyPlanDto>> GenerateDailyPlanAsync()
        => PostAsync<DailyPlanDto>("api/plans/generate", new { });

    /// <summary>Duyệt, sửa hoặc bỏ qua một đề xuất. Duyệt sẽ tạo chương trình khuyến mãi thật.</summary>
    public Task<ApiResponse<bool>> DecideSuggestionAsync(DecideSuggestionRequest req)
        => PostAsync<bool>("api/plans/decide", req);

    // ==========================================================================
    //  KHUYẾN MÃI
    // ==========================================================================

    public Task<ApiResponse<List<PromotionDto>>> GetPromotionsAsync()
        => GetAsync<List<PromotionDto>>("api/admin/promotions");

    /// <summary>Tạm dừng nếu đang chạy, chạy lại nếu đang dừng.</summary>
    public Task<ApiResponse<bool>> TogglePromotionAsync(Guid id)
        => PostAsync<bool>($"api/admin/promotions/{id}/toggle", new { });

    /// <summary>Kết thúc sớm — không bật lại được.</summary>
    public Task<ApiResponse<bool>> StopPromotionAsync(Guid id)
        => PostAsync<bool>($"api/admin/promotions/{id}/stop", new { });

    // ==========================================================================
    //  BÁO CÁO
    // ==========================================================================

    public Task<ApiResponse<ReportDto>> GetReportAsync(string fromDate, string toDate)
        => GetAsync<ReportDto>($"api/admin/reports?from={fromDate}&to={toDate}");

    /// <summary>Bốn con số cho huy hiệu thanh điều hướng. Gọi lại mỗi 60 giây.</summary>
    public Task<ApiResponse<NavBadgesDto>> GetNavBadgesAsync()
        => GetAsync<NavBadgesDto>("api/admin/badges");

    // ==========================================================================
    //  BÁN HÀNG TẠI QUẦY
    // ==========================================================================

    /// <summary>Lưới món cho màn hình bấm đơn. Số ly còn lại đã trừ hàng đang pha.</summary>
    public Task<ApiResponse<PosMenuDto>> GetPosMenuAsync()
        => GetAsync<PosMenuDto>("api/pos/menu");

    /// <summary>
    /// Hỏi trước xem đơn này mất bao lâu, để nhân viên nói ngay với khách.
    /// Gọi lại mỗi lần giỏ đổi — rẻ vì chỉ đọc, không ghi gì.
    /// </summary>
    public Task<ApiResponse<EtaDto>> EstimateEtaAsync(List<EstimateItem> items)
        => PostAsync<EtaDto>("api/pos/estimate", new EstimateRequest(items));

    /// <summary>Chốt đơn tại quầy. Đơn vào thẳng hàng pha.</summary>
    public Task<ApiResponse<PosOrderResultDto>> CreatePosOrderAsync(CreateOrderRequest req)
        => PostAsync<PosOrderResultDto>("api/pos/orders", req);

    // ==========================================================================
    //  MÀN HÌNH PHA CHẾ
    // ==========================================================================

    public Task<ApiResponse<BarQueueDto>> GetBarQueueAsync()
        => GetAsync<BarQueueDto>("api/bar/queue");

    /// <summary>Phiếu pha (định lượng một ly theo size, mức đá, mức đường, topping) của một món trong đơn.</summary>
    public Task<ApiResponse<BarRecipeDto>> GetBrewRecipeAsync(Guid orderItemId)
        => GetAsync<BarRecipeDto>($"api/bar/items/{orderItemId}/recipe");

    public Task<ApiResponse<object>> StartBrewingAsync(Guid orderId)
        => PostAsync<object>($"api/bar/orders/{orderId}/start", new { });

    /// <summary>Bấm hoàn tất — đây là lúc kho bị trừ.</summary>
    public Task<ApiResponse<CompleteOrderResultDto>> CompleteBarOrderAsync(Guid orderId)
        => PostAsync<CompleteOrderResultDto>($"api/bar/orders/{orderId}/complete", new { });

    public Task<ApiResponse<object>> CancelBarOrderAsync(Guid orderId, string reason)
        => PostAsync<object>($"api/bar/orders/{orderId}/cancel", new { reason });

    // ==========================================================================
    //  ẢNH MÓN
    // ==========================================================================

    /// <summary>
    /// Tải ảnh chụp lên cho một món.
    /// <para>
    /// Không dùng <c>PostAsync</c> chung được vì multipart cần Content-Type
    /// riêng có boundary — để JSON serializer đụng vào là hỏng.
    /// </para>
    /// </summary>
    public Task<ApiResponse<ProductImageResult>> UploadProductImageAsync(
        Guid productId, Stream content, string fileName, string contentType)
        => SendAsync<ProductImageResult>(() =>
        {
            var form = new MultipartFormDataContent();
            var part = new StreamContent(content, bufferSize: 81920);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

            // Tên trường BẮT BUỘC là "file" — khớp tham số IFormFile file ở controller
            form.Add(part, "file", fileName);

            return _http.PostAsync($"api/admin/media/product-image?productId={productId}", form);
        });

    /// <summary>Gán ảnh bằng đường dẫn có sẵn. Chuỗi rỗng nghĩa là gỡ ảnh.</summary>
    public Task<ApiResponse<ProductImageResult>> SetProductImageUrlAsync(Guid productId, string? url)
        => PutAsync<ProductImageResult>($"api/admin/media/product-image/{productId}", new { url });

    /// <summary>Gỡ ảnh, quay về dùng hình vẽ SVG.</summary>
    public Task<ApiResponse<bool>> RemoveProductImageAsync(Guid productId)
        => SendAsync<bool>(() => _http.DeleteAsync($"api/admin/media/product-image/{productId}"));

    // ==========================================================================
    //  BÓNG ĐÁ TRỰC TIẾP
    // ==========================================================================

    /// <summary>
    /// Bảng tỉ số hiện tại. Chỉ gọi lúc mở trang và mỗi lần SignalR nối lại —
    /// còn lại máy chủ tự đẩy xuống, không cần hỏi.
    /// </summary>
    public Task<ApiResponse<LiveScoreBoardDto>> GetLiveScoreAsync()
        => GetAsync<LiveScoreBoardDto>("api/live-score");

    /// <summary>
    /// Địa chỉ tuyệt đối của một SignalR hub. Hub nằm ở BACKEND nên phải ghép từ
    /// cùng địa chỉ gốc với API, không phải địa chỉ đang phục vụ frontend.
    /// </summary>
    public Uri HubUrl(string path) => new(_http.BaseAddress!, path);

    // ==========================================================================
    //  DASHBOARD & XÁC THỰC
    // ==========================================================================

    public Task<ApiResponse<DashboardDto>> GetDashboardAsync()
        => GetAsync<DashboardDto>("api/admin/dashboard");

    public Task<ApiResponse<LoginResult>> LoginAsync(LoginRequest req)
        => PostAsync<LoginResult>("api/auth/login", req);

    // ==========================================================================
    //  WORKSHOP PHA CHẾ
    // ==========================================================================

    /// <summary>
    /// Lịch workshop trong một khoảng ngày, kèm bảng ưu đãi.
    /// <para>
    /// Ngày truyền dạng "yyyy-MM-dd" chứ không phải DateTime: buổi học gắn với
    /// ngày theo giờ quán, không phải một mốc UTC.
    /// </para>
    /// </summary>
    public Task<ApiResponse<WorkshopCalendarDto>> GetWorkshopCalendarAsync(string from, string to)
        => GetAsync<WorkshopCalendarDto>($"api/shop/workshop/lich?tu={from}&den={to}");

    public Task<ApiResponse<WorkshopBookingDto>> CreateWorkshopBookingAsync(CreateWorkshopBookingRequest req)
        => PostAsync<WorkshopBookingDto>("api/shop/workshop/dat-cho", req);

    /// <summary>Tra cứu lượt đặt. Số điện thoại là bắt buộc — mã thôi chưa đủ để xem.</summary>
    public Task<ApiResponse<WorkshopBookingDto>> GetWorkshopBookingAsync(string code, string phone)
        => GetAsync<WorkshopBookingDto>(
            $"api/shop/workshop/dat-cho/{Uri.EscapeDataString(code)}?sdt={Uri.EscapeDataString(phone)}");

    public Task<ApiResponse<WorkshopBookingDto>> CancelWorkshopBookingAsync(
        string code, CancelWorkshopBookingRequest req)
        => PostAsync<WorkshopBookingDto>(
            $"api/shop/workshop/dat-cho/{Uri.EscapeDataString(code)}/huy", req);

    // --- Phía quán -------------------------------------------------------------

    public Task<ApiResponse<List<WorkshopBookingDto>>> GetWorkshopBookingsAsync(
        string? from = null, string? to = null, int? status = null)
        => GetAsync<List<WorkshopBookingDto>>(
            $"api/admin/workshop/dat-cho?tu={from}&den={to}&trangThai={status}");

    public Task<ApiResponse<WorkshopBookingDto>> UpdateWorkshopBookingStatusAsync(
        Guid id, int status, string? reason = null)
        => PostAsync<WorkshopBookingDto>(
            $"api/admin/workshop/dat-cho/{id}/trang-thai", new { Status = status, Reason = reason });

    // --- Ca làm việc & két tiền ----------------------------------------------

    /// <summary>Ca đang mở (nếu có) và lần giao ca gần nhất.</summary>
    public Task<ApiResponse<CurrentShiftDto>> GetCurrentShiftAsync()
        => GetAsync<CurrentShiftDto>("api/shifts/current");

    public Task<ApiResponse<ShiftDto>> OpenShiftAsync(OpenShiftRequest req)
        => PostAsync<ShiftDto>("api/shifts/open", req);

    public Task<ApiResponse<ShiftDto>> CloseShiftAsync(CloseShiftRequest req)
        => PostAsync<ShiftDto>("api/shifts/close", req);

    // --- Quầy chấm công -------------------------------------------------------

    /// <summary>Tên nhân viên, khung ca, ai đang trong ca, két đang ở đâu.</summary>
    public Task<ApiResponse<KioskDto>> GetKioskAsync()
        => GetAsync<KioskDto>("api/attendance/kiosk");

    public Task<ApiResponse<CheckInResultDto>> CheckInAsync(CheckInRequest req)
        => PostAsync<CheckInResultDto>("api/attendance/check-in", req);

    public Task<ApiResponse<ShiftDto>> TakeDrawerAsync(TakeDrawerRequest req)
        => PostAsync<ShiftDto>("api/attendance/take-drawer", req);

    public Task<ApiResponse<CheckOutResultDto>> CheckOutAsync(CheckOutRequest req)
        => PostAsync<CheckOutResultDto>("api/attendance/check-out", req);

    // --- Quản lý nhân sự (Manager, Owner) ------------------------------------

    public Task<ApiResponse<List<EmployeeDto>>> GetEmployeesAsync()
        => GetAsync<List<EmployeeDto>>("api/hr/employees");

    public Task<ApiResponse<EmployeeDto>> CreateEmployeeAsync(SaveEmployeeRequest req)
        => PostAsync<EmployeeDto>("api/hr/employees", req);

    public Task<ApiResponse<EmployeeDto>> UpdateEmployeeAsync(Guid id, SaveEmployeeRequest req)
        => PutAsync<EmployeeDto>($"api/hr/employees/{id}", req);

    public Task<ApiResponse<List<WorkSlotDto>>> GetWorkSlotsAsync()
        => GetAsync<List<WorkSlotDto>>("api/hr/slots");

    public Task<ApiResponse<List<WorkSlotDto>>> SaveWorkSlotsAsync(List<SaveWorkSlotRequest> slots)
        => PutAsync<List<WorkSlotDto>>("api/hr/slots", slots);

    /// <summary>Bảng công. Ngày dạng yyyy-MM-dd, giờ Việt Nam.</summary>
    public Task<ApiResponse<TimesheetDto>> GetTimesheetAsync(string from, string to)
        => GetAsync<TimesheetDto>($"api/hr/timesheet?tu={from}&den={to}");

    /// <summary>Đối soát két theo ca. Ngày dạng yyyy-MM-dd, giờ Việt Nam. Chỉ quản lý.</summary>
    public Task<ApiResponse<ShiftReportDto>> GetShiftReportAsync(string from, string to)
        => GetAsync<ShiftReportDto>($"api/shifts/report?tu={from}&den={to}");

    /// <summary>Lịch workshop phía quán — cùng dữ liệu với trang khách, kèm cả buổi đã kín chỗ.</summary>
    public Task<ApiResponse<WorkshopCalendarDto>> GetAdminWorkshopCalendarAsync(
        string? from = null, string? to = null)
        => GetAsync<WorkshopCalendarDto>($"api/admin/workshop/lich?tu={from}&den={to}");

    /// <summary>Sửa giá và sức chứa một buổi. Chỉ Manager/Owner gọi được.</summary>
    public Task<ApiResponse<WorkshopSessionSavedDto>> UpdateWorkshopSessionAsync(
        Guid id, UpdateWorkshopSessionRequest req)
        => PostAsync<WorkshopSessionSavedDto>($"api/admin/workshop/lich/{id}", req);

    // ==========================================================================
    //  LỚP GỌI HTTP DÙNG CHUNG
    // ==========================================================================

    private Task<ApiResponse<T>> GetAsync<T>(string url)
        => SendAsync<T>(() => _http.GetAsync(url));

    private Task<ApiResponse<T>> PostAsync<T>(string url, object body)
        => SendAsync<T>(() => _http.PostAsJsonAsync(url, body));

    private Task<ApiResponse<T>> PutAsync<T>(string url, object body)
        => SendAsync<T>(() => _http.PutAsJsonAsync(url, body));

    /// <summary>
    /// PATCH cho những thay đổi chỉ đụng MỘT trường, như đổi giá bán một món.
    /// HttpClient không có PatchAsJsonAsync nên phải dựng request bằng tay.
    /// </summary>
    private Task<ApiResponse<T>> PatchAsync<T>(string url, object body)
        => SendAsync<T>(() => _http.PatchAsync(url,
            System.Net.Http.Json.JsonContent.Create(body)));

    /// <summary>
    /// Bọc mọi lời gọi HTTP: bắt lỗi mạng, đọc lỗi nghiệp vụ từ backend,
    /// và trả về cùng một hình dạng để trang xử lý thống nhất.
    /// </summary>
    private async Task<ApiResponse<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            var response = await send();

            if (response.IsSuccessStatusCode)
            {
                var ok = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOpts);
                return ok ?? ApiResponse<T>.Fail("EMPTY_RESPONSE", "Máy chủ không trả về dữ liệu.");
            }

            // Backend trả lỗi nghiệp vụ có cấu trúc — đọc để hiển thị đúng thông điệp
            var error = await TryReadErrorAsync<T>(response);
            if (error is not null) return error;

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized =>
                    ApiResponse<T>.Fail("UNAUTHORIZED", "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại."),
                System.Net.HttpStatusCode.Forbidden =>
                    ApiResponse<T>.Fail("FORBIDDEN", "Tài khoản của bạn không có quyền thực hiện việc này."),
                System.Net.HttpStatusCode.NotFound =>
                    ApiResponse<T>.Fail("NOT_FOUND", "Không tìm thấy dữ liệu."),
                _ =>
                    ApiResponse<T>.Fail("SERVER_ERROR", "Máy chủ đang gặp sự cố. Vui lòng thử lại sau.")
            };
        }
        catch (HttpRequestException)
        {
            return ApiResponse<T>.Fail("NETWORK_ERROR",
                "Không kết nối được máy chủ. Kiểm tra đường truyền rồi thử lại.");
        }
        catch (TaskCanceledException)
        {
            return ApiResponse<T>.Fail("TIMEOUT",
                "Máy chủ phản hồi quá lâu. Vui lòng thử lại.");
        }
        catch (Exception ex)
        {
            return ApiResponse<T>.Fail("UNKNOWN", $"Lỗi không xác định: {ex.Message}");
        }
    }

    private static async Task<ApiResponse<T>?> TryReadErrorAsync<T>(HttpResponseMessage response)
    {
        try { return await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOpts); }
        catch { return null; }
    }
}
