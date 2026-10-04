using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QlyCoffee.Application.Services;
using QlyCoffee.Shared;

namespace QlyCoffee.Api.Controllers;

// ==============================================================================
//  WORKSHOP — ĐẶT LỊCH
//
//  Hai controller, hai mức quyền:
//    · WorkshopController       công khai, khách không đăng nhập vẫn đặt được.
//    · AdminWorkshopController  cần đăng nhập, dành cho quán.
//
//  Tách hai lớp chứ không dùng một lớp với [AllowAnonymous] rải rác: chỉ cần
//  quên một thuộc tính là danh sách khách đặt chỗ — có tên, số điện thoại, email
//  — trở thành công khai.
// ==============================================================================

[Route("api/shop/workshop")]
[AllowAnonymous]
public class WorkshopController : BaseApiController
{
    private readonly IWorkshopService _workshop;

    public WorkshopController(IWorkshopService workshop) => _workshop = workshop;

    /// <summary>
    /// Lịch các buổi trong một khoảng ngày, kèm bảng ưu đãi.
    /// <para>
    /// Không có tham số thì trả 8 tuần tính từ hôm nay — đủ cho lịch hai tháng
    /// mà không kéo cả bảng về.
    /// </para>
    /// </summary>
    [HttpGet("lich")]
    public async Task<IActionResult> GetCalendar(
        [FromQuery] string? tu, [FromQuery] string? den, CancellationToken ct)
    {
        var from = ParseDate(tu) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var to = ParseDate(den) ?? from.AddDays(56);

        var data = await _workshop.GetCalendarAsync(CurrentStoreId, from, to, ct);
        return Ok(data);
    }

    /// <summary>Giữ chỗ. Không cần đăng nhập, không cần gọi điện.</summary>
    [HttpPost("dat-cho")]
    public async Task<IActionResult> CreateBooking(
        [FromBody] CreateWorkshopBookingRequest req, CancellationToken ct)
    {
        try
        {
            var booking = await _workshop.CreateBookingAsync(CurrentStoreId, req, ct);
            return Ok(booking);
        }
        catch (BusinessRuleException ex)
        {
            return Fail<WorkshopBookingDto>(400, "DAT_CHO_KHONG_HOP_LE", ex.Message);
        }
    }

    /// <summary>
    /// Tra cứu một lượt đặt.
    /// <para>
    /// Số điện thoại là BẮT BUỘC, không phải tùy chọn: mã đặt chỗ chỉ có 6 ký tự,
    /// dò được. Thiếu số điện thoại thì ai cũng xem được thông tin liên hệ của
    /// khách khác.
    /// </para>
    /// </summary>
    [HttpGet("dat-cho/{code}")]
    public async Task<IActionResult> GetBooking(
        string code, [FromQuery] string? sdt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sdt))
            return Fail<WorkshopBookingDto>(400, "THIEU_SO_DIEN_THOAI",
                "Nhập số điện thoại đã dùng khi đặt để tra cứu.");

        var booking = await _workshop.GetBookingAsync(CurrentStoreId, code, sdt, ct);

        // Sai mã và sai số điện thoại trả về CÙNG MỘT câu: phân biệt hai trường
        // hợp là chỉ cho người dò biết mã nào có thật.
        if (booking is null)
            return Fail<WorkshopBookingDto>(404, "KHONG_TIM_THAY",
                "Không tìm thấy lượt đặt nào khớp mã và số điện thoại này.");

        return Ok(booking);
    }

    /// <summary>Khách tự hủy chỗ.</summary>
    [HttpPost("dat-cho/{code}/huy")]
    public async Task<IActionResult> CancelBooking(
        string code, [FromBody] CancelWorkshopBookingRequest req, CancellationToken ct)
    {
        try
        {
            var booking = await _workshop.CancelBookingAsync(
                CurrentStoreId, code, req.Phone, req.Reason, ct);
            return Ok(booking);
        }
        catch (BusinessRuleException ex)
        {
            return Fail<WorkshopBookingDto>(400, "HUY_KHONG_HOP_LE", ex.Message);
        }
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
}

// ==============================================================================

[Route("api/admin/workshop")]
[Authorize]
public class AdminWorkshopController : BaseApiController
{
    private readonly IWorkshopService _workshop;

    public AdminWorkshopController(IWorkshopService workshop) => _workshop = workshop;

    /// <summary>Danh sách khách đã đặt, để quán chuẩn bị dụng cụ và gọi xác nhận.</summary>
    [HttpGet("dat-cho")]
    public async Task<IActionResult> GetBookings(
        [FromQuery] string? tu, [FromQuery] string? den, [FromQuery] int? trangThai,
        CancellationToken ct)
    {
        var list = await _workshop.GetBookingsForAdminAsync(
            CurrentStoreId, ParseDate(tu), ParseDate(den), trangThai, ct);
        return Ok(list);
    }

    /// <summary>Xác nhận, điểm danh, đánh dấu không tới hoặc hủy giúp khách.</summary>
    [HttpPost("dat-cho/{id:guid}/trang-thai")]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromBody] UpdateWorkshopStatusRequest req, CancellationToken ct)
    {
        try
        {
            var booking = await _workshop.UpdateBookingStatusAsync(
                CurrentStoreId, id, req.Status, req.Reason, ct);
            return Ok(booking);
        }
        catch (BusinessRuleException ex)
        {
            return Fail<WorkshopBookingDto>(400, "DOI_TRANG_THAI_KHONG_HOP_LE", ex.Message);
        }
    }

    /// <summary>Lịch phía quán — dùng chung dữ liệu với trang khách để không lệch nhau.</summary>
    [HttpGet("lich")]
    public async Task<IActionResult> GetCalendar(
        [FromQuery] string? tu, [FromQuery] string? den, CancellationToken ct)
    {
        var from = ParseDate(tu) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var to = ParseDate(den) ?? from.AddDays(56);

        var data = await _workshop.GetCalendarAsync(CurrentStoreId, from, to, ct);
        return Ok(data);
    }

    /// <summary>
    /// Sửa giá, sức chứa và mô tả một buổi.
    /// <para>
    /// Chỉ Manager và Owner. Nhân viên phục vụ (Staff) xem được lịch và điểm danh
    /// khách, nhưng đổi giá là quyết định kinh doanh — vai trò khác, quyền khác.
    /// </para>
    /// </summary>
    [HttpPost("lich/{id:guid}")]
    [Authorize(Roles = "Manager,Owner")]
    public async Task<IActionResult> UpdateSession(
        Guid id, [FromBody] UpdateWorkshopSessionRequest req, CancellationToken ct)
    {
        try
        {
            var saved = await _workshop.UpdateSessionAsync(CurrentStoreId, id, req, ct);
            return Ok(saved);
        }
        catch (BusinessRuleException ex)
        {
            return Fail<WorkshopSessionSavedDto>(400, "SUA_BUOI_KHONG_HOP_LE", ex.Message);
        }
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
}

/// <summary>Đổi trạng thái một lượt đặt, từ phía quán.</summary>
public class UpdateWorkshopStatusRequest
{
    /// <summary>0 chờ xác nhận, 1 đã xác nhận, 2 đã tới, 3 đã hủy, 4 không tới.</summary>
    public int Status { get; set; }

    /// <summary>Bắt buộc khi hủy — khách sẽ đọc được câu này.</summary>
    public string? Reason { get; set; }
}
