using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QlyCoffee.Application.Services;
using QlyCoffee.Shared;

namespace QlyCoffee.Api.Controllers;

// ==============================================================================
//  CA LÀM VIỆC & ĐỐI SOÁT KÉT
//
//  Nhân viên: xem két hiện tại, nhận ca, đếm két và giao ca.
//  Quản lý:   thêm báo cáo đối soát nhiều ca (doanh thu, tiền nộp, hụt két).
//
//  Nhân viên KHÔNG xem được báo cáo đối soát — nó lộ doanh thu của cả quán và
//  chênh lệch két của đồng nghiệp. Họ chỉ thấy ca đang mở và lần giao gần nhất.
// ==============================================================================

[Route("api/shifts")]
[Authorize(Roles = "Staff,Manager,Owner")]
public class ShiftsController : BaseApiController
{
    private readonly IShiftService _shifts;

    public ShiftsController(IShiftService shifts) => _shifts = shifts;

    /// <summary>Ca đang mở (nếu có) và lần giao ca gần nhất.</summary>
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct)
        => Ok(await _shifts.GetCurrentAsync(CurrentStoreId, CurrentUserId, ct));

    /// <summary>Nhận ca: ghi người nhận và số tiền đếm được trong két.</summary>
    [HttpPost("open")]
    public async Task<IActionResult> Open([FromBody] OpenShiftRequest req, CancellationToken ct)
    {
        if (CurrentUserId is not Guid userId)
            return Fail<ShiftDto>(401, "CHUA_DANG_NHAP", "Phiên đăng nhập đã hết hạn.");
        try
        {
            return Ok(await _shifts.OpenAsync(CurrentStoreId, userId, req, ct));
        }
        catch (BusinessRuleException ex)
        {
            return Fail<ShiftDto>(400, "KHONG_NHAN_CA_DUOC", ex.Message);
        }
    }

    /// <summary>Đóng ca: đếm két, để lại tiền cho ca sau, giao cho người kế tiếp.</summary>
    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] CloseShiftRequest req, CancellationToken ct)
    {
        if (CurrentUserId is not Guid userId)
            return Fail<ShiftDto>(401, "CHUA_DANG_NHAP", "Phiên đăng nhập đã hết hạn.");
        try
        {
            return Ok(await _shifts.CloseAsync(CurrentStoreId, userId, req, ct));
        }
        catch (BusinessRuleException ex)
        {
            return Fail<ShiftDto>(400, "KHONG_GIAO_CA_DUOC", ex.Message);
        }
    }

    /// <summary>
    /// Đối soát két theo ca trong khoảng ngày (giờ Việt Nam). Mặc định 7 ngày gần nhất.
    /// Chỉ quản lý và chủ quán.
    /// </summary>
    [HttpGet("report")]
    [Authorize(Roles = "Manager,Owner")]
    public async Task<IActionResult> Report([FromQuery] string? tu, [FromQuery] string? den, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var to = ParseDate(den) ?? today;
        var from = ParseDate(tu) ?? to.AddDays(-6);

        return Ok(await _shifts.GetReportAsync(CurrentStoreId, from, to, ct));
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
}
