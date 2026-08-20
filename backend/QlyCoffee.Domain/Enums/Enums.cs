namespace QlyCoffee.Domain.Enums;

// ==============================================================================
//  TẬP HỢP CÁC KIỂU LIỆT KÊ DÙNG TOÀN HỆ THỐNG
//
//  QUY TẮC QUAN TRỌNG:
//  - Mọi enum đều gán GIÁ TRỊ SỐ TƯỜNG MINH. Không bao giờ đổi số của giá trị
//    đã tồn tại, vì số này được lưu xuống database. Thêm mới thì gán số kế tiếp.
//  - Enum được lưu xuống DB dưới dạng int (không phải string) để tiết kiệm và
//    tránh lỗi chính tả. Tên hiển thị tiếng Việt nằm ở lớp Display.
// ==============================================================================

/// <summary>
/// Vai trò người dùng trong hệ thống. Quyết định quyền truy cập màn hình và API.
/// </summary>
public enum UserRole
{
    /// <summary>Khách hàng — chỉ đặt món và xem đơn của chính mình.</summary>
    Customer = 0,

    /// <summary>Nhân viên — xử lý đơn, nhập kho, ghi hao hụt. Không xem báo cáo tài chính.</summary>
    Staff = 1,

    /// <summary>Quản lý — toàn quyền vận hành, duyệt kế hoạch AI, xem báo cáo.</summary>
    Manager = 2,

    /// <summary>Chủ quán — toàn quyền, bao gồm cả quản lý tài khoản và cấu hình hệ thống.</summary>
    Owner = 3
}

// ------------------------------------------------------------------------------
//  KHO & NGUYÊN LIỆU
// ------------------------------------------------------------------------------

/// <summary>
/// Đơn vị CƠ SỞ dùng để lưu trữ và tính toán tồn kho.
/// <para>
/// QUY TẮC BẤT DI BẤT DỊCH: mọi số lượng lưu trong database (công thức, tồn kho,
/// sổ cái) đều theo đơn vị này. Việc quy đổi từ đơn vị mua hàng (kg, lít, thùng)
/// chỉ xảy ra ở màn hình nhập liệu, không bao giờ ở tầng lưu trữ.
/// </para>
/// </summary>
public enum BaseUnit
{
    /// <summary>Gram — nguyên liệu khô: cà phê, trà, bột, trân châu, trái cây.</summary>
    Gram = 0,

    /// <summary>Mililit — chất lỏng: sữa, siro, nước đường, kem.</summary>
    Milliliter = 1,

    /// <summary>Cái/chiếc — đếm được: ly, nắp, ống hút, quả trứng, quả chanh.</summary>
    Piece = 2
}

/// <summary>
/// Nhóm phân loại nguyên liệu. Dùng để lọc trong màn hình kho và tô màu biểu đồ.
/// </summary>
public enum IngredientCategory
{
    /// <summary>Cà phê hạt, cà phê rang xay.</summary>
    Coffee = 0,

    /// <summary>Các loại trà: hồng trà, ô long, trà lài, trà xanh.</summary>
    Tea = 1,

    /// <summary>Sữa và chế phẩm từ sữa: sữa tươi, sữa đặc, kem béo, kem cheese.</summary>
    Dairy = 2,

    /// <summary>Siro, sốt, nước đường, kem muối.</summary>
    Syrup = 3,

    /// <summary>Trái cây tươi và trái cây ngâm.</summary>
    Fruit = 4,

    /// <summary>Topping: trân châu, thạch, pudding, kem cheese.</summary>
    Topping = 5,

    /// <summary>Bột: matcha, cacao, bột kem béo.</summary>
    Powder = 6,

    /// <summary>Chất tạo ngọt: đường cát, mật ong, đường phèn.</summary>
    Sweetener = 7,

    /// <summary>Bao bì: ly, nắp, ống hút, túi, tem.</summary>
    Packaging = 8,

    /// <summary>Khác: đá viên, nước lọc, trứng.</summary>
    Other = 9
}

/// <summary>
/// Trạng thái của một lô hàng trong kho.
/// </summary>
public enum LotStatus
{
    /// <summary>Đang dùng được. Chỉ lô ở trạng thái này mới được FEFO lấy ra dùng.</summary>
    Active = 0,

    /// <summary>Đã dùng hết (RemainingQuantity = 0). Giữ lại để đối soát, không xóa.</summary>
    Depleted = 1,

    /// <summary>Đã quá hạn sử dụng. Job kiểm tra hạn dùng buổi sáng tự chuyển sang trạng thái này.</summary>
    Expired = 2,

    /// <summary>Đã tiêu hủy thực tế, đã ghi bút toán hao hụt.</summary>
    Disposed = 3
}

/// <summary>
/// Loại bút toán trong sổ cái kho.
/// <para>
/// QUY ƯỚC DẤU: <c>QuantityDelta</c> DƯƠNG là nhập kho, ÂM là xuất kho.
/// Tổng cộng dồn <c>QuantityDelta</c> theo một nguyên liệu phải luôn bằng tồn kho thực tế.
/// </para>
/// </summary>
public enum MovementType
{
    /// <summary>Nhập hàng từ nhà cung cấp. Dấu DƯƠNG. Tạo lô mới.</summary>
    PurchaseIn = 0,

    /// <summary>Xuất kho do bán hàng, trừ theo công thức định lượng. Dấu ÂM.</summary>
    SaleOut = 1,

    /// <summary>Hao hụt, đổ bỏ do pha hỏng hoặc rơi vãi. Dấu ÂM. Bắt buộc có lý do.</summary>
    Waste = 2,

    /// <summary>Tiêu hủy do quá hạn sử dụng. Dấu ÂM. Do job tự động sinh.</summary>
    ExpiredOut = 3,

    /// <summary>Kiểm kê phát hiện THỪA so với sổ sách. Dấu DƯƠNG.</summary>
    AdjustIn = 4,

    /// <summary>Kiểm kê phát hiện THIẾU so với sổ sách. Dấu ÂM.</summary>
    AdjustOut = 5,

    /// <summary>Hoàn kho do hủy đơn đã xác nhận. Dấu DƯƠNG. Đảo ngược đúng lô đã trừ.</summary>
    ReturnIn = 6,

    /// <summary>Xuất nguyên liệu thô để chế biến bán thành phẩm (VD: nấu trân châu). Dấu ÂM.</summary>
    ProductionOut = 7,

    /// <summary>Nhập bán thành phẩm đã chế biến vào kho. Dấu DƯƠNG.</summary>
    ProductionIn = 8
}

// ------------------------------------------------------------------------------
//  ĐƠN HÀNG
// ------------------------------------------------------------------------------

/// <summary>
/// Trạng thái đơn hàng.
/// <para>
/// ĐIỂM QUAN TRỌNG NHẤT: kho được trừ khi chuyển sang <see cref="Confirmed"/>,
/// và được hoàn lại khi chuyển sang <see cref="Cancelled"/> (nếu đã trừ).
/// Không có trạng thái nào khác động vào kho.
/// </para>
/// </summary>
public enum OrderStatus
{
    /// <summary>Khách vừa đặt, chưa xác nhận. CHƯA trừ kho.</summary>
    Pending = 0,

    /// <summary>Đã xác nhận — ĐÃ TRỪ KHO tại thời điểm này.</summary>
    Confirmed = 1,

    /// <summary>Đang pha chế. Không động vào kho.</summary>
    Preparing = 2,

    /// <summary>Đã pha xong, chờ khách nhận. Không động vào kho.</summary>
    Ready = 3,

    /// <summary>Đã giao cho khách, đơn hoàn tất. Không động vào kho.</summary>
    Completed = 4,

    /// <summary>Đã hủy. Nếu trước đó đã trừ kho thì hệ thống tự hoàn lại đúng lô.</summary>
    Cancelled = 5
}

/// <summary>Hình thức phục vụ.</summary>
public enum OrderType
{
    /// <summary>Uống tại quán — dùng ly sứ/thủy tinh, không tốn bao bì mang đi.</summary>
    DineIn = 0,

    /// <summary>Mang đi — có tính ly nhựa, nắp, ống hút vào công thức.</summary>
    Takeaway = 1
}

/// <summary>Phương thức thanh toán được hỗ trợ ở giai đoạn đầu.</summary>
public enum PaymentMethod
{
    /// <summary>Tiền mặt tại quầy.</summary>
    Cash = 0,

    /// <summary>Chuyển khoản ngân hàng qua mã VietQR.</summary>
    BankTransfer = 1
}

/// <summary>Trạng thái thanh toán của đơn.</summary>
public enum PaymentStatus
{
    /// <summary>Chưa thanh toán.</summary>
    Unpaid = 0,

    /// <summary>Đã thanh toán đủ.</summary>
    Paid = 1,

    /// <summary>Đã hoàn tiền cho khách.</summary>
    Refunded = 2
}

// ------------------------------------------------------------------------------
//  KHUYẾN MÃI & KẾ HOẠCH AI
// ------------------------------------------------------------------------------

/// <summary>Cách tính giảm giá của một chương trình khuyến mãi.</summary>
public enum PromotionType
{
    /// <summary>Giảm theo phần trăm. Trường Value chứa số phần trăm (VD: 20 = giảm 20%).</summary>
    PercentOff = 0,

    /// <summary>Giảm số tiền cố định. Trường Value chứa số đồng (VD: 10000 = giảm 10.000đ).</summary>
    AmountOff = 1,

    /// <summary>Combo mua kèm. Cấu hình chi tiết nằm trong trường ComboConfig dạng JSON.</summary>
    Combo = 2
}

/// <summary>Trạng thái vòng đời của chương trình khuyến mãi.</summary>
public enum PromotionStatus
{
    /// <summary>Bản nháp, chưa áp dụng.</summary>
    Draft = 0,

    /// <summary>Đang chạy — khách nhìn thấy giá đã giảm.</summary>
    Active = 1,

    /// <summary>Tạm dừng — có thể bật lại.</summary>
    Paused = 2,

    /// <summary>Đã hết hạn — không áp dụng nữa.</summary>
    Expired = 3
}

/// <summary>
/// Loại đề xuất mà AI Engine sinh ra trong bản kế hoạch cuối ngày.
/// </summary>
public enum SuggestionType
{
    /// <summary>Giảm giá để đẩy nhanh nguyên liệu sắp hết hạn.</summary>
    Discount = 0,

    /// <summary>Làm combo thay vì giảm sâu — dùng khi mức giảm cần thiết quá lớn.</summary>
    Bundle = 1,

    /// <summary>Nhân viên chủ động mời khách — dùng khi giảm giá không đủ hiệu quả.</summary>
    StaffPush = 2,

    /// <summary>Giảm lượng nhập kỳ tới — nguyên liệu liên tục dư thừa.</summary>
    ReducePurchase = 3,

    /// <summary>Không cứu được, chuẩn bị tiêu hủy và ghi nhận hao hụt.</summary>
    Dispose = 4,

    /// <summary>Sắp hết hàng, cần nhập thêm.</summary>
    Restock = 5
}

/// <summary>Mức ưu tiên xử lý của một đề xuất.</summary>
public enum SuggestionPriority
{
    /// <summary>Thấp — theo dõi, chưa cần hành động.</summary>
    Low = 0,

    /// <summary>Trung bình — nên xử lý trong tuần.</summary>
    Medium = 1,

    /// <summary>Cao — nên xử lý trong 1-2 ngày.</summary>
    High = 2,

    /// <summary>Khẩn cấp — phải xử lý ngay hôm nay.</summary>
    Critical = 3
}

/// <summary>Hành động của người quản lý đối với một đề xuất từ AI.</summary>
public enum DecisionAction
{
    /// <summary>Duyệt nguyên đề xuất — hệ thống tự tạo chương trình khuyến mãi.</summary>
    Approved = 0,

    /// <summary>Từ chối — không làm gì.</summary>
    Rejected = 1,

    /// <summary>Duyệt nhưng sửa tham số (mức giảm, thời gian áp dụng).</summary>
    Modified = 2
}

/// <summary>Trạng thái xử lý của một bản kế hoạch hằng ngày.</summary>
public enum PlanStatus
{
    /// <summary>Mới sinh, chưa ai xem.</summary>
    New = 0,

    /// <summary>Quản lý đã xem và ra quyết định.</summary>
    Reviewed = 1,

    /// <summary>Đã lưu trữ, không hiển thị ở danh sách chính.</summary>
    Archived = 2
}

/// <summary>
/// Mức độ nghiêm trọng của rủi ro hết hạn — dùng để tô màu và sắp xếp.
/// </summary>
public enum RiskSeverity
{
    /// <summary>Thấp — còn nhiều thời gian, giá trị nhỏ.</summary>
    Low = 0,

    /// <summary>Trung bình — cần theo dõi.</summary>
    Medium = 1,

    /// <summary>Cao — nên hành động.</summary>
    High = 2,

    /// <summary>Khẩn cấp — hết hạn trong 1 ngày hoặc giá trị rất lớn.</summary>
    Critical = 3
}
