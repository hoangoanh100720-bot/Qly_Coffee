using QlyCoffee.Domain.Enums;

namespace QlyCoffee.Domain.Entities;

// ==============================================================================
//  NHÓM TÀI KHOẢN & XÁC THỰC
//
//  Dùng JWT tự triển khai (không dùng ASP.NET Identity đầy đủ) vì nhu cầu đơn giản:
//  đăng nhập bằng email/mật khẩu, phân bốn vai trò, không cần xác thực hai lớp
//  hay đăng nhập mạng xã hội ở giai đoạn đầu.
// ==============================================================================

/// <summary>
/// Tài khoản người dùng — bao gồm cả khách hàng và nhân sự của quán.
/// </summary>
public class User : SoftDeletableEntity
{
    /// <summary>Email đăng nhập. Duy nhất toàn hệ thống, luôn lưu chữ thường.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Số điện thoại. Duy nhất nếu có. Dùng để tra cứu khách hàng thân thiết.</summary>
    public string? Phone { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Mật khẩu đã băm bằng BCrypt (work factor 12).
    /// TUYỆT ĐỐI không lưu mật khẩu gốc, không dùng MD5/SHA1.
    /// <c>null</c> nếu tài khoản chỉ đăng nhập qua mạng xã hội.
    /// </summary>
    public string? PasswordHash { get; set; }

    public string? AvatarUrl { get; set; }

    /// <summary>Vai trò — quyết định toàn bộ quyền truy cập.</summary>
    public UserRole Role { get; set; } = UserRole.Customer;

    /// <summary>Chi nhánh mà nhân sự này làm việc. <c>null</c> với khách hàng.</summary>
    public Guid? StoreId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

/// <summary>
/// Refresh token để cấp lại access token mà không bắt đăng nhập lại.
/// <para>
/// Áp dụng cơ chế xoay vòng: mỗi lần dùng sẽ thu hồi token cũ và cấp token mới.
/// Nếu một token đã thu hồi bị dùng lại thì đó là dấu hiệu bị đánh cắp —
/// hệ thống thu hồi toàn bộ token của người dùng đó.
/// </para>
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>Chuỗi token ngẫu nhiên 64 byte, mã hóa base64url.</summary>
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Thời điểm bị thu hồi. <c>null</c> nghĩa là còn hiệu lực.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Token thay thế khi xoay vòng — dùng để lần vết chuỗi token.</summary>
    public string? ReplacedByToken { get; set; }

    /// <summary>IP tạo token — hỗ trợ điều tra khi nghi ngờ bị chiếm tài khoản.</summary>
    public string? CreatedByIp { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;

    public User? User { get; set; }
}
