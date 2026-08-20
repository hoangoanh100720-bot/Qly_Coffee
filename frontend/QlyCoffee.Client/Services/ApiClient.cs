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

    public Task<ApiResponse<InventoryLotDto>> ReceiveStockAsync(ReceiveStockRequest req)
        => PostAsync<InventoryLotDto>("api/inventory/receive", req);

    public Task<ApiResponse<bool>> RecordWasteAsync(RecordWasteRequest req)
        => PostAsync<bool>("api/inventory/waste", req);

    /// <summary>Tiêu hủy lô quá hạn và ghi bút toán hao hụt tương ứng.</summary>
    public Task<ApiResponse<bool>> DisposeLotAsync(Guid lotId)
        => PostAsync<bool>($"api/inventory/lots/{lotId}/dispose", new { });

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
    public async Task<(ProductAdminDto? Product, List<RecipeLineDto>? Lines, List<IngredientStockDto>? Ingredients)>
        GetRecipeEditorDataAsync(Guid productId)
    {
        var res = await GetAsync<RecipeEditorData>($"api/admin/products/{productId}/recipe");
        return res.Success && res.Data is not null
            ? (res.Data.Product, res.Data.Lines, res.Data.Ingredients)
            : (null, null, null);
    }

    private record RecipeEditorData(
        ProductAdminDto Product,
        List<RecipeLineDto> Lines,
        List<IngredientStockDto> Ingredients);

    public Task<ApiResponse<bool>> SaveRecipeAsync(Guid productId, object payload)
        => PutAsync<bool>($"api/admin/products/{productId}/recipe", payload);

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
    //  DASHBOARD & XÁC THỰC
    // ==========================================================================

    public Task<ApiResponse<DashboardDto>> GetDashboardAsync()
        => GetAsync<DashboardDto>("api/admin/dashboard");

    public Task<ApiResponse<LoginResult>> LoginAsync(LoginRequest req)
        => PostAsync<LoginResult>("api/auth/login", req);

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
