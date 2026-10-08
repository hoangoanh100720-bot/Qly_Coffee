using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QlyCoffee.Application.Services;
using QlyCoffee.Shared;

namespace QlyCoffee.Api.Controllers;

// ==============================================================================
//  QUẦY CHẤM CÔNG (api/attendance) — máy quầy đăng nhập tài khoản nhân viên.
//  Từng người tự xác minh bằng mã NV + PIN trong thân yêu cầu; tài khoản của
//  máy chỉ chứng minh "đây là máy của quán", không chứng minh ai đang đứng.
//
//  QUẢN LÝ NHÂN SỰ (api/hr) — chỉ Manager, Owner. Lương và bảng công là thông
//  tin nhạy cảm, nhân viên không xem được của nhau.
// ==============================================================================

[Route("api/attendance")]
[Authorize(Roles = "Staff,Manager,Owner")]
public class AttendanceController : BaseApiController
{
    private readonly IStaffService _staff;

    public AttendanceController(IStaffService staff) => _staff = staff;

    /// <summary>Danh sách tên, khung ca, ai đang trong ca, két đang ở đâu.</summary>
    [HttpGet("kiosk")]
    public async Task<IActionResult> Kiosk(CancellationToken ct)
        => Ok(await _staff.GetKioskAsync(CurrentStoreId, CurrentUserId, ct));

    [HttpPost("check-in")]
    public Task<IActionResult> CheckIn([FromBody] CheckInRequest req, CancellationToken ct)
        => Run<CheckInResultDto>("KHONG_CHAM_CONG_VAO_DUOC",
            userId => _staff.CheckInAsync(CurrentStoreId, userId, req, ct));

    [HttpPost("take-drawer")]
    public Task<IActionResult> TakeDrawer([FromBody] TakeDrawerRequest req, CancellationToken ct)
        => Run<ShiftDto>("KHONG_NHAN_KET_DUOC",
            userId => _staff.TakeDrawerAsync(CurrentStoreId, userId, req, ct));

    [HttpPost("check-out")]
    public Task<IActionResult> CheckOut([FromBody] CheckOutRequest req, CancellationToken ct)
        => Run<CheckOutResultDto>("KHONG_CHAM_CONG_RA_DUOC",
            userId => _staff.CheckOutAsync(CurrentStoreId, userId, req, ct));

    private async Task<IActionResult> Run<T>(string errorCode, Func<Guid, Task<T>> action)
    {
        if (CurrentUserId is not Guid userId)
            return Fail<T>(401, "CHUA_DANG_NHAP", "Phiên đăng nhập của máy đã hết hạn.");
        try
        {
            return Ok(await action(userId));
        }
        catch (BusinessRuleException ex)
        {
            return Fail<T>(400, errorCode, ex.Message);
        }
    }
}

[Route("api/hr")]
[Authorize(Roles = "Manager,Owner")]
public class HrController : BaseApiController
{
    private readonly IStaffService _staff;

    public HrController(IStaffService staff) => _staff = staff;

    [HttpGet("employees")]
    public async Task<IActionResult> Employees(CancellationToken ct)
        => Ok(await _staff.GetEmployeesAsync(CurrentStoreId, ct));

    [HttpPost("employees")]
    public async Task<IActionResult> Create([FromBody] SaveEmployeeRequest req, CancellationToken ct)
    {
        try { return Ok(await _staff.CreateEmployeeAsync(CurrentStoreId, req, ct)); }
        catch (BusinessRuleException ex) { return Fail<EmployeeDto>(400, "NHAN_VIEN_KHONG_HOP_LE", ex.Message); }
    }

    [HttpPut("employees/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveEmployeeRequest req, CancellationToken ct)
    {
        try { return Ok(await _staff.UpdateEmployeeAsync(CurrentStoreId, id, req, ct)); }
        catch (BusinessRuleException ex) { return Fail<EmployeeDto>(400, "NHAN_VIEN_KHONG_HOP_LE", ex.Message); }
    }

    [HttpGet("slots")]
    public async Task<IActionResult> Slots(CancellationToken ct)
        => Ok(await _staff.GetSlotsAsync(CurrentStoreId, ct));

    [HttpPut("slots")]
    public async Task<IActionResult> SaveSlots([FromBody] List<SaveWorkSlotRequest> slots, CancellationToken ct)
    {
        try { return Ok(await _staff.SaveSlotsAsync(CurrentStoreId, slots, ct)); }
        catch (BusinessRuleException ex) { return Fail<List<WorkSlotDto>>(400, "KHUNG_CA_KHONG_HOP_LE", ex.Message); }
    }

    /// <summary>Bảng công trong khoảng ngày (giờ Việt Nam). Mặc định từ đầu tháng tới hôm nay.</summary>
    [HttpGet("timesheet")]
    public async Task<IActionResult> Timesheet([FromQuery] string? tu, [FromQuery] string? den, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var to = ParseDate(den) ?? today;
        var from = ParseDate(tu) ?? new DateOnly(to.Year, to.Month, 1);
        return Ok(await _staff.GetTimesheetAsync(CurrentStoreId, from, to, ct));
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
}
