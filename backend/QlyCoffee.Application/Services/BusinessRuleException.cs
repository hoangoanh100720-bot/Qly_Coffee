namespace QlyCoffee.Application.Services;

/// <summary>
/// Vi phạm quy tắc nghiệp vụ — thứ mà người dùng sửa được bằng cách thao tác lại:
/// đơn không có món nào, hủy đơn mà không nhập lý do, chuyển trạng thái sai thứ tự…
/// Controller bắt loại này và trả HTTP 400 kèm thông báo tiếng Việt.
///
/// TẠI SAO KHÔNG DÙNG InvalidOperationException:
/// EF Core và Npgsql ném InvalidOperationException cho lỗi HẠ TẦNG (mất kết nối,
/// xung đột execution strategy, LINQ không dịch được). Nếu controller bắt chung
/// một kiểu thì sự cố database sẽ hiện ra thành "chuyển trạng thái không hợp lệ"
/// với mã 400 — nhân viên tưởng mình bấm sai, còn lỗi thật thì không ai biết.
/// Tách riêng ra thì lỗi hạ tầng nổi lên thành 500 và vào log đúng như nó là.
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}
