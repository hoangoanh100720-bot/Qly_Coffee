namespace QlyCoffee.Domain;

/// <summary>
/// Sinh GUID phiên bản 7 (có timestamp ở đầu nên sắp xếp được theo thời gian).
/// <para>
/// .NET 9 có sẵn <c>GuidV7.New()</c>, nhưng dự án đang chạy trên .NET 8
/// nên tự cài đặt theo đúng chuẩn RFC 9562.
/// </para>
/// <para>
/// VÌ SAO DÙNG V7 THAY VÌ V4 NGẪU NHIÊN THUẦN:
/// khóa chính sắp xếp được theo thời gian giúp PostgreSQL chèn dữ liệu vào cuối
/// chỉ mục B-tree thay vì chèn ngẫu nhiên giữa chừng — ghi nhanh hơn đáng kể và
/// ít phân mảnh chỉ mục hơn khi bảng lớn dần.
/// </para>
/// </summary>
public static class GuidV7
{
    /// <summary>
    /// Bố cục 128 bit theo RFC 9562:
    ///   48 bit đầu  — timestamp Unix tính bằng mili giây
    ///    4 bit      — số phiên bản (7)
    ///   12 bit      — ngẫu nhiên
    ///    2 bit      — biến thể (10)
    ///   62 bit      — ngẫu nhiên
    /// </summary>
    public static Guid New()
    {
        Span<byte> bytes = stackalloc byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 48 bit timestamp, big-endian
        bytes[0] = (byte)(timestamp >> 40);
        bytes[1] = (byte)(timestamp >> 32);
        bytes[2] = (byte)(timestamp >> 24);
        bytes[3] = (byte)(timestamp >> 16);
        bytes[4] = (byte)(timestamp >> 8);
        bytes[5] = (byte)timestamp;

        // Đặt số phiên bản = 7 vào 4 bit cao của byte thứ 6
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);

        // Đặt biến thể = 10 vào 2 bit cao của byte thứ 8
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes);
    }
}
