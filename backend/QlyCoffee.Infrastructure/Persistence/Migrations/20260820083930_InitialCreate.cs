using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QlyCoffee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "Ngày kinh doanh yyyy-MM-dd theo giờ VN. Lưu chuỗi để tránh lỗi lệch múi giờ."),
                    generated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    total_value_at_risk = table.Column<int>(type: "integer", nullable: false, comment: "Tổng giá trị nguyên liệu có nguy cơ phải bỏ (đồng). Con số nổi bật nhất của UI."),
                    critical_count = table.Column<int>(type: "integer", nullable: false),
                    warning_count = table.Column<int>(type: "integer", nullable: false),
                    low_stock_count = table.Column<int>(type: "integer", nullable: false),
                    revenue = table.Column<int>(type: "integer", nullable: false),
                    order_count = table.Column<int>(type: "integer", nullable: false),
                    waste_value = table.Column<int>(type: "integer", nullable: false),
                    risk_analysis_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Toàn bộ kết quả phân tích, khớp List<LotRiskDto>. Dùng vẽ biểu đồ."),
                    headline = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ai_model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ai_tokens_used = table.Column<int>(type: "integer", nullable: false),
                    ai_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Lỗi gọi AI. Có lỗi vẫn phải tạo kế hoạch đủ số liệu — lỗi AI không làm mất kế hoạch."),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_plans", x => x.id);
                },
                comment: "BẢN KẾ HOẠCH HẰNG NGÀY do AI Engine sinh lúc chốt sổ. Các cột số do BACKEND tính; các cột chữ (headline, summary) do Claude viết. AI không bao giờ được tính toán số liệu tiền/lượng.");

            migrationBuilder.CreateTable(
                name: "modifier_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    min_select = table.Column<int>(type: "integer", nullable: false),
                    max_select = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modifier_groups", x => x.id);
                },
                comment: "Nhóm tùy chọn: Topping, Mức đường, Mức đá.");

            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    slug = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    open_time = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false, comment: "Định dạng HH:mm"),
                    close_time = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false, comment: "Định dạng HH:mm"),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Múi giờ IANA. Mọi phép tính theo ngày kinh doanh phải dùng giá trị này."),
                    business_day_end_hour = table.Column<int>(type: "integer", nullable: false, comment: "Giờ chốt sổ (0-23). Job sinh kế hoạch chạy sau mốc này."),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stores", x => x.id);
                },
                comment: "Chi nhánh. MVP chỉ có một bản ghi, nhưng thiết kế sẵn cho nhiều chi nhánh.");

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    lead_time_days = table.Column<int>(type: "integer", nullable: false, comment: "Số ngày từ lúc đặt tới lúc nhận. Dùng tính điểm đặt hàng lại."),
                    min_order_value = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.id);
                },
                comment: "Nhà cung cấp nguyên liệu.");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Email đăng nhập, luôn lưu chữ thường, duy nhất toàn hệ thống."),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, comment: "Số điện thoại VN. Dữ liệu cá nhân — phải xóa được theo yêu cầu (NĐ 13/2023)."),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true, comment: "Băm bằng BCrypt work factor 12. Không bao giờ lưu mật khẩu gốc."),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<int>(type: "integer", nullable: false, comment: "0=Customer 1=Staff 2=Manager 3=Owner. Quyết định toàn bộ quyền truy cập."),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                },
                comment: "Tài khoản người dùng: khách hàng và nhân sự của quán.");

            migrationBuilder.CreateTable(
                name: "modifiers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price_delta = table.Column<int>(type: "integer", nullable: false),
                    color_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modifiers", x => x.id);
                    table.ForeignKey(
                        name: "FK_modifiers_modifier_groups_modifier_group_id",
                        column: x => x.modifier_group_id,
                        principalTable: "modifier_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Một lựa chọn cụ thể trong nhóm tùy chọn.");

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    color_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false, comment: "Màu đại diện danh mục, hex #RRGGBB. Lấy từ màu đặc trưng của nhóm đồ uống."),
                    icon_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, comment: "Mã icon SVG dựng sẵn ở frontend, không dùng file ảnh ngoài."),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                    table.ForeignKey(
                        name: "FK_categories_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Danh mục món trên menu.");

            migrationBuilder.CreateTable(
                name: "ingredients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    sku = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false, comment: "Mã nội bộ [NHÓM]-[TÊN]-[SỐ]. VD: DAI-MILK-01"),
                    category = table.Column<int>(type: "integer", nullable: false),
                    base_unit = table.Column<int>(type: "integer", nullable: false, comment: "0=Gram 1=Milliliter 2=Piece. MỌI số lượng của nguyên liệu này theo đơn vị đây."),
                    color_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false, comment: "Màu THẬT của nguyên liệu. Là DỮ LIỆU: dùng vẽ minh họa SVG và chấm màu bảng kho."),
                    icon_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, comment: "Mã hình SVG dựng sẵn ở frontend: milk, coffee-beans, boba, peach..."),
                    min_stock_level = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false, comment: "Ngưỡng cảnh báo đỏ, theo đơn vị cơ sở."),
                    reorder_point = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false, comment: "Điểm đặt hàng lại = tồn tối thiểu + (tiêu thụ TB/ngày × số ngày giao hàng NCC)."),
                    default_shelf_life_days = table.Column<int>(type: "integer", nullable: true, comment: "Số ngày HSD mặc định kể từ ngày nhập. NULL = không có hạn (đá, ly nhựa)."),
                    expiry_warning_days = table.Column<int>(type: "integer", nullable: false, comment: "Còn bao nhiêu ngày thì coi là cận hạn. Trái cây tươi để 2-3, đồ khô để 14-30."),
                    wastage_rate = table.Column<double>(type: "double precision", precision: 6, scale: 4, nullable: false, comment: "Hao hụt chế biến. 0.03 = 3%. Trái cây gọt vỏ 0.10-0.15, bột/siro 0.01."),
                    average_unit_cost = table.Column<int>(type: "integer", nullable: false, comment: "Giá vốn bình quân gia quyền, ĐỒNG cho 1 đơn vị cơ sở. Tự cập nhật khi nhập kho."),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingredients", x => x.id);
                    table.ForeignKey(
                        name: "FK_ingredients_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "ĐỊNH NGHĨA nguyên liệu. Không chứa số lượng — số lượng nằm ở inventory_lots vì mỗi lô có hạn dùng và giá vốn riêng.");

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ordered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    total_cost = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    is_ai_generated = table.Column<bool>(type: "boolean", nullable: false, comment: "TRUE nếu do AI sinh từ đề xuất Restock. Dùng đo hiệu quả gợi ý nhập hàng."),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_orders", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_orders_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Phiếu đặt mua gửi nhà cung cấp.");

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Mã đơn QC-YYMMDD-NNNN. Khách tra cứu đơn bằng mã này, không cần đăng nhập."),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Dữ liệu cá nhân — phải xóa được theo yêu cầu (NĐ 13/2023)."),
                    order_type = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    payment_method = table.Column<int>(type: "integer", nullable: false),
                    payment_status = table.Column<int>(type: "integer", nullable: false),
                    subtotal = table.Column<int>(type: "integer", nullable: false),
                    discount_total = table.Column<int>(type: "integer", nullable: false),
                    grand_total = table.Column<int>(type: "integer", nullable: false),
                    cost_total = table.Column<int>(type: "integer", nullable: false, comment: "Giá vốn THỰC TẾ từ đúng những lô đã bị trừ, không phải giá vốn bình quân."),
                    applied_promotion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_deducted = table.Column<bool>(type: "boolean", nullable: false, comment: "Cờ chống trừ kho hai lần khi client retry hoặc người dùng bấm nhanh."),
                    stock_returned = table.Column<bool>(type: "boolean", nullable: false, comment: "Cờ chống hoàn kho hai lần khi hủy đơn."),
                    placed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ready_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.id);
                    table.ForeignKey(
                        name: "FK_orders_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                },
                comment: "Đơn hàng. KHO ĐƯỢC TRỪ khi chuyển sang Confirmed(1) và HOÀN LẠI khi Cancelled(5). Không trạng thái nào khác động vào kho.");

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token = table.Column<string>(type: "text", nullable: true),
                    created_by_ip = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Token làm mới phiên đăng nhập. Xoay vòng mỗi lần dùng để chống đánh cắp.");

            migrationBuilder.CreateTable(
                name: "stock_counts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    counted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    is_finalized = table.Column<bool>(type: "boolean", nullable: false, comment: "Đã chốt sổ thì khóa vĩnh viễn và đã sinh bút toán điều chỉnh."),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_counts", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_counts_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id");
                },
                comment: "Phiếu kiểm kê. Chênh lệch sinh bút toán AdjustIn/AdjustOut.");

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    slug = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    color_primary_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false, comment: "Màu THẬT của ly nước, hex. Dùng dựng minh họa SVG khi chưa có ảnh chụp."),
                    color_accent_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false, comment: "Màu lớp kem/lớp trên. Kết hợp với màu chính tạo hình ly hai lớp giống thật."),
                    base_price = table.Column<int>(type: "integer", nullable: false, comment: "Giá bán size mặc định, đơn vị ĐỒNG (int, không thập phân)."),
                    computed_cost = table.Column<int>(type: "integer", nullable: false, comment: "Giá vốn tính từ công thức: Σ(lượng × giá vốn TB nguyên liệu × (1+hao hụt))."),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_featured = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    unavailable_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    max_servings = table.Column<int>(type: "integer", nullable: false, comment: "Số ly tối đa còn làm được = min(tồn kho / định mức) trên nguyên liệu bắt buộc."),
                    tags = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false, comment: "Nhãn ngăn cách bởi dấu phẩy. VD: best-seller,mới"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                    table.ForeignKey(
                        name: "FK_products_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Món trên menu. Giá vốn (computed_cost) là số DẪN XUẤT từ công thức, không nhập tay.");

            migrationBuilder.CreateTable(
                name: "daily_consumptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity_used = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false),
                    cost_used = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_consumptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_daily_consumptions_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Tiêu thụ nguyên liệu theo ngày, tổng hợp sẵn. Tồn tại để mô hình dự báo EWMA không phải quét sổ cái hàng trăm nghìn dòng.");

            migrationBuilder.CreateTable(
                name: "inventory_lots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "Mã lô [SKU]-[YYMMDD]-[chữ cái]. Tự sinh nếu không nhập."),
                    received_quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false),
                    remaining_quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false, comment: "CHỈ được sửa trong transaction có SELECT...FOR UPDATE, nếu không sẽ tồn kho âm."),
                    unit_cost = table.Column<int>(type: "integer", nullable: false, comment: "Giá vốn LỊCH SỬ của lô này (đồng/đơn vị cơ sở). Không đổi theo thời gian."),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expiry_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "Trường quan trọng nhất bảng: quyết định thứ tự FEFO và cảnh báo lãng phí."),
                    status = table.Column<int>(type: "integer", nullable: false, comment: "0=Active 1=Depleted 2=Expired 3=Disposed. Chỉ lô Active mới được FEFO lấy ra."),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purchase_order_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_lots", x => x.id);
                    table.ForeignKey(
                        name: "FK_inventory_lots_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_lots_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id");
                },
                comment: "MỘT LÔ HÀNG cụ thể. Phải tách lô vì mỗi lần nhập có HSD và giá khác nhau — đây là nền tảng của FEFO và của toàn bộ tính năng cảnh báo cận hạn.");

            migrationBuilder.CreateTable(
                name: "modifier_recipe_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false, comment: "KHÔNG nhân hệ số size — thêm topping vào size L vẫn cùng lượng."),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modifier_recipe_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_modifier_recipe_items_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_modifier_recipe_items_modifiers_modifier_id",
                        column: x => x.modifier_id,
                        principalTable: "modifiers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Công thức riêng của topping. VD: Trân châu đen → trừ thêm 40g trân châu.");

            migrationBuilder.CreateTable(
                name: "purchase_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    conversion_quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false, comment: "Số đơn vị cơ sở trong MỘT đơn vị mua."),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_units", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_units_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Đơn vị mua hàng và hệ số quy đổi sang đơn vị cơ sở. VD: Thùng 12 hộp 1L = 12000 ml.");

            migrationBuilder.CreateTable(
                name: "purchase_order_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false),
                    unit_cost = table.Column<int>(type: "integer", nullable: false),
                    received_quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false, comment: "Số nhận thực tế. Lệch so với quantity nghĩa là NCC giao thiếu."),
                    expiry_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_order_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchase_order_items_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_purchase_order_items_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_count_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_count_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    system_quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false),
                    counted_quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false),
                    variance_quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false, comment: "= counted − system. ÂM nghĩa là thiếu hụt (có thể do hao hụt hoặc thất thoát)."),
                    variance_cost = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_count_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_count_lines_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_stock_count_lines_stock_counts_stock_count_id",
                        column: x => x.stock_count_id,
                        principalTable: "stock_counts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "daily_sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    units_sold = table.Column<int>(type: "integer", nullable: false),
                    revenue = table.Column<int>(type: "integer", nullable: false),
                    cost = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_sales", x => x.id);
                    table.ForeignKey(
                        name: "FK_daily_sales_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Doanh số theo món theo ngày. Dùng tính số ly bán TB/ngày khi sinh phương án khuyến mãi.");

            migrationBuilder.CreateTable(
                name: "plan_suggestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    daily_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reasoning = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    banner_copy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    discount_percent = table.Column<int>(type: "integer", nullable: true),
                    target_units = table.Column<int>(type: "integer", nullable: true),
                    expected_waste_avoided = table.Column<int>(type: "integer", nullable: true),
                    expected_net_benefit = table.Column<int>(type: "integer", nullable: true, comment: "= cứu được + lãi thêm − lãi mất trên số ly vốn dĩ đã bán được giá gốc."),
                    days_until_expiry = table.Column<int>(type: "integer", nullable: true),
                    quantity_at_risk = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: true),
                    suggested_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    suggested_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "FK_plan_suggestions_daily_plans_daily_plan_id",
                        column: x => x.daily_plan_id,
                        principalTable: "daily_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_plan_suggestions_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_plan_suggestions_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id");
                },
                comment: "Đề xuất hành động. Cột chữ do AI viết, cột số do backend tính.");

            migrationBuilder.CreateTable(
                name: "product_modifiers",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modifier_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_modifiers", x => new { x.product_id, x.modifier_group_id });
                    table.ForeignKey(
                        name: "FK_product_modifiers_modifier_groups_modifier_group_id",
                        column: x => x.modifier_group_id,
                        principalTable: "modifier_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_product_modifiers_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Bảng nối: món nào có nhóm tùy chọn nào.");

            migrationBuilder.CreateTable(
                name: "product_variants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    price_delta = table.Column<int>(type: "integer", nullable: false),
                    recipe_multiplier = table.Column<double>(type: "double precision", precision: 6, scale: 3, nullable: false, comment: "Hệ số nhân công thức gốc. Size L thường 1.4. KHÔNG áp dụng cho topping."),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_variants", x => x.id);
                    table.ForeignKey(
                        name: "FK_product_variants_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Biến thể món (size). recipe_multiplier CHỈ nhân công thức gốc, KHÔNG nhân topping.");

            migrationBuilder.CreateTable(
                name: "promotions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<int>(type: "integer", nullable: false, comment: "PercentOff → số %; AmountOff → số đồng."),
                    status = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    max_redemptions = table.Column<int>(type: "integer", nullable: true),
                    redemption_count = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    banner_text = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true, comment: "Dòng chữ trên banner trang bán hàng. Do AI viết, tối đa 80 ký tự."),
                    source_plan_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Kế hoạch AI nào sinh ra khuyến mãi này — dùng đo hiệu quả của AI."),
                    source_suggestion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.id);
                    table.ForeignKey(
                        name: "FK_promotions_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                },
                comment: "Chương trình khuyến mãi.");

            migrationBuilder.CreateTable(
                name: "recipe_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false, comment: "Lượng cho MỘT ly size mặc định, theo đơn vị cơ sở của nguyên liệu."),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false, comment: "Nguyên liệu tùy chọn KHÔNG tính vào phép kiểm tra còn làm được bao nhiêu ly."),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipe_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_recipe_items_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recipe_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "CÔNG THỨC ĐỊNH LƯỢNG — bảng nối giữa MENU và KHO. Không có bảng này thì không thể tự trừ tồn kho, không tính được giá vốn.");

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false, comment: "0=PurchaseIn 1=SaleOut 2=Waste 3=ExpiredOut 4=AdjustIn 5=AdjustOut 6=ReturnIn 7=ProductionOut 8=ProductionIn"),
                    quantity_delta = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false, comment: "DƯƠNG = nhập kho, ÂM = xuất kho. Không bao giờ bằng 0."),
                    unit_cost = table.Column<int>(type: "integer", nullable: false),
                    total_cost = table.Column<int>(type: "integer", nullable: false),
                    reference_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true, comment: "Loại chứng từ nguồn: ORDER | PURCHASE | COUNT | WASTE | EXPIRY"),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true, comment: "BẮT BUỘC với bút toán hao hụt và điều chỉnh kiểm kê."),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "Quy ước: ORDER:{orderId}:ING:{ingredientId}:LOT:{lotId}"),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_movements_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_inventory_lots_lot_id",
                        column: x => x.lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id");
                },
                comment: "SỔ CÁI KHO — CHỈ GHI THÊM. Không bao giờ UPDATE hay DELETE. Ghi sai thì ghi bút toán đảo, giống nguyên tắc kế toán kép. Cộng dồn quantity_delta phải khớp tồn kho thực tế.");

            migrationBuilder.CreateTable(
                name: "plan_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    daily_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    suggestion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    modified_payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_promotion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_decisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_plan_decisions_daily_plans_daily_plan_id",
                        column: x => x.daily_plan_id,
                        principalTable: "daily_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_plan_decisions_plan_suggestions_suggestion_id",
                        column: x => x.suggestion_id,
                        principalTable: "plan_suggestions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_plan_decisions_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id");
                },
                comment: "Quyết định của quản lý. Vừa là nhật ký kiểm toán, vừa là dữ liệu đo chất lượng AI (tỷ lệ duyệt là chỉ số tin cậy quan trọng nhất).");

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    variant_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<int>(type: "integer", nullable: false),
                    line_total = table.Column<int>(type: "integer", nullable: false),
                    modifiers_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Snapshot topping đã chọn: [{modifierId,name,priceDelta}]"),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_items_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_order_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Dòng món trong đơn. Tên và giá là SNAPSHOT tại thời điểm đặt — đổi giá hay đổi tên món sau này không được làm sai đơn cũ.");

            migrationBuilder.CreateIndex(
                name: "IX_categories_slug",
                table: "categories",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_categories_store_id_is_active_sort_order",
                table: "categories",
                columns: new[] { "store_id", "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_daily_consumptions_ingredient_id",
                table: "daily_consumptions",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_daily_consumptions_store_id_ingredient_id_business_date",
                table: "daily_consumptions",
                columns: new[] { "store_id", "ingredient_id", "business_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_daily_plans_store_id_business_date",
                table: "daily_plans",
                columns: new[] { "store_id", "business_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_daily_plans_store_id_generated_at",
                table: "daily_plans",
                columns: new[] { "store_id", "generated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_daily_sales_product_id",
                table: "daily_sales",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_daily_sales_store_id_product_id_business_date",
                table: "daily_sales",
                columns: new[] { "store_id", "product_id", "business_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ingredients_sku",
                table: "ingredients",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ingredients_store_id_category",
                table: "ingredients",
                columns: new[] { "store_id", "category" });

            migrationBuilder.CreateIndex(
                name: "IX_ingredients_store_id_is_active",
                table: "ingredients",
                columns: new[] { "store_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_lots_ingredient_id",
                table: "inventory_lots",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_lots_store_id_lot_code",
                table: "inventory_lots",
                columns: new[] { "store_id", "lot_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_lots_supplier_id",
                table: "inventory_lots",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_lots_expiry_watch",
                table: "inventory_lots",
                columns: new[] { "store_id", "expiry_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_lots_fefo",
                table: "inventory_lots",
                columns: new[] { "store_id", "ingredient_id", "status", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "IX_modifier_recipe_items_ingredient_id",
                table: "modifier_recipe_items",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_modifier_recipe_items_modifier_id_ingredient_id",
                table: "modifier_recipe_items",
                columns: new[] { "modifier_id", "ingredient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_modifiers_modifier_group_id",
                table: "modifiers",
                column: "modifier_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_order_id",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_product_id",
                table: "order_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_variant_id",
                table: "order_items",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_orders_code",
                table: "orders",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_customer_phone",
                table: "orders",
                column: "customer_phone");

            migrationBuilder.CreateIndex(
                name: "IX_orders_store_id_placed_at",
                table: "orders",
                columns: new[] { "store_id", "placed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_store_id_status_placed_at",
                table: "orders",
                columns: new[] { "store_id", "status", "placed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_user_id",
                table: "orders",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_decisions_actor_id",
                table: "plan_decisions",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_decisions_daily_plan_id",
                table: "plan_decisions",
                column: "daily_plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_decisions_suggestion_id",
                table: "plan_decisions",
                column: "suggestion_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_suggestions_daily_plan_id_priority",
                table: "plan_suggestions",
                columns: new[] { "daily_plan_id", "priority" });

            migrationBuilder.CreateIndex(
                name: "IX_plan_suggestions_ingredient_id",
                table: "plan_suggestions",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_plan_suggestions_product_id",
                table: "plan_suggestions",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_modifiers_modifier_group_id",
                table: "product_modifiers",
                column: "modifier_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_variants_product_id",
                table: "product_variants",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_category_id",
                table: "products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_slug",
                table: "products",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_store_id_category_id_is_active",
                table: "products",
                columns: new[] { "store_id", "category_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_products_store_id_is_available_sort_order",
                table: "products",
                columns: new[] { "store_id", "is_available", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_promotions_active_window",
                table: "promotions",
                columns: new[] { "store_id", "status", "starts_at", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "IX_promotions_product_id",
                table: "promotions",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_items_ingredient_id",
                table: "purchase_order_items",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_items_purchase_order_id",
                table: "purchase_order_items",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_code",
                table: "purchase_orders",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_supplier_id",
                table: "purchase_orders",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_units_ingredient_id",
                table: "purchase_units",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_items_ingredient",
                table: "recipe_items",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_recipe_items_product_id_ingredient_id",
                table: "recipe_items",
                columns: new[] { "product_id", "ingredient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_token",
                table: "refresh_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_lines_ingredient_id",
                table: "stock_count_lines",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_lines_stock_count_id",
                table: "stock_count_lines",
                column: "stock_count_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_actor_id",
                table: "stock_counts",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_code",
                table: "stock_counts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_movements_reference",
                table: "stock_movements",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_actor_id",
                table: "stock_movements",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ingredient_id",
                table: "stock_movements",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_lot_id",
                table: "stock_movements",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_store_id_ingredient_id_occurred_at",
                table: "stock_movements",
                columns: new[] { "store_id", "ingredient_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_store_id_type_occurred_at",
                table: "stock_movements",
                columns: new[] { "store_id", "type", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ux_movements_idempotency",
                table: "stock_movements",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_stores_slug",
                table: "stores",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_phone",
                table: "users",
                column: "phone",
                unique: true,
                filter: "phone IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_consumptions");

            migrationBuilder.DropTable(
                name: "daily_sales");

            migrationBuilder.DropTable(
                name: "modifier_recipe_items");

            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "plan_decisions");

            migrationBuilder.DropTable(
                name: "product_modifiers");

            migrationBuilder.DropTable(
                name: "promotions");

            migrationBuilder.DropTable(
                name: "purchase_order_items");

            migrationBuilder.DropTable(
                name: "purchase_units");

            migrationBuilder.DropTable(
                name: "recipe_items");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "stock_count_lines");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "modifiers");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "product_variants");

            migrationBuilder.DropTable(
                name: "plan_suggestions");

            migrationBuilder.DropTable(
                name: "purchase_orders");

            migrationBuilder.DropTable(
                name: "stock_counts");

            migrationBuilder.DropTable(
                name: "inventory_lots");

            migrationBuilder.DropTable(
                name: "modifier_groups");

            migrationBuilder.DropTable(
                name: "daily_plans");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "ingredients");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "stores");
        }
    }
}
