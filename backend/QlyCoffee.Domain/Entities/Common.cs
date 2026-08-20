namespace QlyCoffee.Domain.Entities;

// ==============================================================================
//  LỚP CƠ SỞ CHO MỌI THỰC THỂ
//
//  Mọi bảng nghiệp vụ đều kế thừa từ đây để có sẵn khóa chính, dấu thời gian
//  và cơ chế xóa mềm. EF Core được cấu hình để tự động lọc bỏ bản ghi đã xóa mềm
//  (xem AppDbContext.OnModelCreating → HasQueryFilter).
// ==============================================================================

/// <summary>
/// Lớp cơ sở cho thực thể có khóa chính và dấu thời gian tạo/sửa.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Khóa chính. Dùng GUID v7 (sinh ở tầng ứng dụng) thay vì số tự tăng vì:
    /// (1) sinh được ở client khi offline, (2) không lộ số lượng bản ghi,
    /// (3) vẫn sắp xếp được theo thời gian nhờ v7 có timestamp ở đầu.
    /// </summary>
    public Guid Id { get; set; } = GuidV7.New();

    /// <summary>Thời điểm tạo bản ghi. Luôn lưu UTC, quy đổi giờ VN khi hiển thị.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm sửa gần nhất. EF tự cập nhật qua SaveChangesInterceptor.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Lớp cơ sở cho thực thể hỗ trợ xóa mềm.
/// <para>
/// KHÔNG BAO GIỜ xóa cứng dữ liệu nghiệp vụ — vì đơn hàng cũ vẫn tham chiếu tới
/// món và nguyên liệu đã ngừng bán. Xóa cứng sẽ làm hỏng báo cáo lịch sử.
/// </para>
/// </summary>
public abstract class SoftDeletableEntity : BaseEntity
{
    /// <summary>
    /// Thời điểm xóa mềm. <c>null</c> nghĩa là bản ghi còn hiệu lực.
    /// Query filter của EF sẽ tự thêm điều kiện <c>DeletedAt == null</c> vào mọi truy vấn.
    /// Muốn lấy cả bản ghi đã xóa thì gọi <c>.IgnoreQueryFilters()</c>.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Ai đã xóa — phục vụ đối soát khi có tranh chấp.</summary>
    public Guid? DeletedByUserId { get; set; }
}

/// <summary>
/// Lớp cơ sở cho thực thể thuộc về một chi nhánh cụ thể.
/// <para>
/// Ở giai đoạn MVP chỉ có một chi nhánh, nhưng cột này vẫn tồn tại từ đầu để
/// khi mở rộng nhiều chi nhánh chỉ cần thêm bộ lọc, không phải migrate lại dữ liệu.
/// </para>
/// </summary>
public abstract class StoreScopedEntity : SoftDeletableEntity
{
    /// <summary>Chi nhánh sở hữu bản ghi này.</summary>
    public Guid StoreId { get; set; }
}
