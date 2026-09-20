using System.Globalization;

namespace QlyCoffee.Client.Services;

/// <summary>
/// Định dạng tiền Việt Nam.
/// <para>
/// Toàn hệ thống lưu tiền dưới dạng <c>int</c> đơn vị ĐỒNG — không dùng
/// <c>decimal</c> hay <c>double</c> vì tiền Việt không có phần thập phân và
/// số thực sẽ tích lũy sai số khi cộng dồn hàng nghìn dòng đơn hàng.
/// </para>
/// </summary>
public static class Money
{
    /// <summary>Dùng dấu chấm ngăn cách hàng nghìn theo chuẩn Việt Nam.</summary>
    private static readonly NumberFormatInfo VnFormat = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = new[] { 3 }
    };

    /// <summary>
    /// Định dạng đầy đủ kèm ký hiệu tiền. VD: <c>45000</c> → <c>"45.000đ"</c>.
    /// </summary>
    public static string Format(int amount) =>
        amount.ToString("N0", VnFormat) + "đ";

    /// <summary>
    /// Định dạng không kèm ký hiệu — dùng trong ô nhập liệu và cột bảng
    /// đã có tiêu đề ghi rõ đơn vị.
    /// </summary>
    public static string FormatPlain(int amount) =>
        amount.ToString("N0", VnFormat);

    /// <summary>
    /// Rút gọn cho biểu đồ và thẻ số liệu chật chỗ.
    /// VD: <c>1240000</c> → <c>"1,24tr"</c>; <c>45000</c> → <c>"45k"</c>.
    /// </summary>
    public static string FormatShort(int amount)
    {
        var abs = Math.Abs(amount);
        var sign = amount < 0 ? "−" : "";

        return abs switch
        {
            >= 1_000_000_000 => $"{sign}{abs / 1_000_000_000.0:0.##} tỷ",
            >= 1_000_000     => $"{sign}{abs / 1_000_000.0:0.##}tr",
            >= 1_000         => $"{sign}{abs / 1_000.0:0.#}k",
            _                => $"{sign}{abs}đ"
        };
    }

    /// <summary>
    /// Định dạng có dấu, dùng cho bảng lãi lỗ.
    /// VD: <c>+336.000đ</c> hoặc <c>−180.000đ</c>.
    /// </summary>
    public static string FormatSigned(int amount) =>
        amount switch
        {
            > 0 => "+" + Format(amount),
            < 0 => "−" + Format(Math.Abs(amount)),
            _   => Format(0)
        };

    /// <summary>
    /// Đọc số tiền người dùng gõ vào, bỏ qua dấu chấm phân cách và chữ "đ".
    /// Trả về 0 nếu không đọc được — an toàn hơn là ném lỗi giữa lúc gõ.
    /// </summary>
    public static int Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0;

        var cleaned = new string(input.Where(char.IsDigit).ToArray());
        return int.TryParse(cleaned, out var value) ? value : 0;
    }
}

/// <summary>Định dạng số lượng nguyên liệu.</summary>
public static class Qty
{
    /// <summary>
    /// Số lượng lớn thì bỏ phần thập phân cho gọn; số nhỏ giữ tối đa hai chữ số
    /// thập phân vì 0,5 quả chanh khác hẳn 1 quả chanh.
    /// </summary>
    public static string Format(double value) =>
        value >= 100 ? value.ToString("N0") : value.ToString("0.##");

    /// <summary>Ký hiệu đơn vị hiển thị theo <c>BaseUnit</c> trong database.</summary>
    public static string UnitLabel(int baseUnit) => baseUnit switch
    {
        0 => "g",
        1 => "ml",
        2 => "cái",
        _ => ""
    };

    /// <summary>Gộp số và đơn vị: <c>"180 ml"</c>.</summary>
    public static string WithUnit(double value, string unitLabel) =>
        $"{Format(value)} {unitLabel}";
}
