using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QlyCoffee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DatLichWorkshop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workshop_discounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    percent = table.Column<int>(type: "integer", nullable: false, comment: "Phần trăm giảm trên giá buổi. Ghi bằng % chứ không bằng tiền vì mỗi buổi một giá."),
                    min_seats = table.Column<int>(type: "integer", nullable: false, comment: "Chỉ với ưu đãi cả lượt: số chỗ tối thiểu. 0 = không ràng buộc."),
                    min_days_ahead = table.Column<int>(type: "integer", nullable: false, comment: "Chỉ với ưu đãi cả lượt: phải đặt trước bao nhiêu ngày. 0 = không ràng buộc."),
                    requires_proof = table.Column<bool>(type: "boolean", nullable: false, comment: "Hệ thống KHÔNG xác minh được diện ưu đãi — cờ này để giao diện nói thẳng là cần trình thẻ."),
                    proof_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workshop_discounts", x => x.id);
                },
                comment: "Ưu đãi workshop. Là DỮ LIỆU chứ không phải enum cứng: đổi khuyến mãi là quyết định kinh doanh hằng tháng, không được bắt build lại hệ thống.");

            migrationBuilder.CreateTable(
                name: "workshop_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    summary = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    session_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Ngày diễn ra theo giờ quán. Kiểu date — KHÔNG có múi giờ, nên không bị lệch ngày."),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false, comment: "Số chỗ tối đa. Giới hạn bởi số BỘ DỤNG CỤ, không phải số ghế."),
                    base_price = table.Column<int>(type: "integer", nullable: false, comment: "Giá một chỗ, giá thường chưa ưu đãi, đơn vị đồng."),
                    status = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Ghi chú nội bộ, KHÔNG hiện cho khách."),
                    cancel_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workshop_sessions", x => x.id);
                },
                comment: "Một BUỔI workshop cụ thể trên lịch. Lịch lặp hằng tuần được sinh thành từng bản ghi rời chứ không lưu quy tắc lặp — buổi nào cũng có thể dời giờ, đổi chủ đề, hạ giá hoặc hủy riêng nó.");

            migrationBuilder.CreateTable(
                name: "workshop_bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Vừa là cách liên hệ, vừa là mật khẩu để tra cứu và hủy — biết mã thôi chưa đủ."),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    seats = table.Column<int>(type: "integer", nullable: false, comment: "Tổng số chỗ. Lưu dư ra để đếm chỗ trống chỉ cần cộng một cột, không phải nối bảng dòng."),
                    subtotal = table.Column<int>(type: "integer", nullable: false),
                    booking_discount_id = table.Column<Guid>(type: "uuid", nullable: true),
                    booking_discount_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    booking_discount_amount = table.Column<int>(type: "integer", nullable: false),
                    grand_total = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    checked_in_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workshop_bookings", x => x.id);
                    table.ForeignKey(
                        name: "FK_workshop_bookings_workshop_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "workshop_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Một lượt đặt chỗ. Mọi con số tiền được CHỤP LẠI lúc đặt, không tính lại khi đọc — đổi giá tuần sau không được làm đổi số tiền đã báo cho khách tuần này.");

            migrationBuilder.CreateTable(
                name: "workshop_booking_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discount_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tier_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, comment: "Tên diện giá chụp lại lúc đặt. 'Giá thường' khi không có ưu đãi."),
                    percent = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<int>(type: "integer", nullable: false, comment: "Giá một chỗ SAU ưu đãi theo chỗ, đồng."),
                    line_total = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workshop_booking_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_workshop_booking_lines_workshop_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "workshop_bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Mấy chỗ theo diện ưu đãi nào. Tách dòng vì một nhóm có thể gồm cả sinh viên lẫn người đi làm — gán một diện cho cả lượt thì hoặc giảm thừa, hoặc mất khách.");

            migrationBuilder.CreateIndex(
                name: "IX_workshop_booking_lines_booking_id",
                table: "workshop_booking_lines",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_workshop_bookings_dem_cho",
                table: "workshop_bookings",
                columns: new[] { "session_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_workshop_bookings_sdt",
                table: "workshop_bookings",
                columns: new[] { "store_id", "phone" });

            migrationBuilder.CreateIndex(
                name: "ux_workshop_bookings_ma",
                table: "workshop_bookings",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_workshop_discounts_ma",
                table: "workshop_discounts",
                columns: new[] { "store_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workshop_sessions_lich",
                table: "workshop_sessions",
                columns: new[] { "store_id", "session_date", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workshop_booking_lines");

            migrationBuilder.DropTable(
                name: "workshop_discounts");

            migrationBuilder.DropTable(
                name: "workshop_bookings");

            migrationBuilder.DropTable(
                name: "workshop_sessions");
        }
    }
}
