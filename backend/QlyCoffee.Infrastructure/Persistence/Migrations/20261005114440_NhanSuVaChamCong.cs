using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QlyCoffee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NhanSuVaChamCong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "closed_by_employee_id",
                table: "cash_shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "handed_over_to_employee_id",
                table: "cash_shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "opened_by_employee_id",
                table: "cash_shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    full_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    position = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    hourly_wage = table.Column<int>(type: "integer", nullable: false, comment: "Lương theo giờ, đồng."),
                    pin_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "PIN chấm công băm BCrypt. Không bao giờ lưu PIN gốc."),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    hired_on = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    failed_pin_attempts = table.Column<int>(type: "integer", nullable: false),
                    pin_locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "Sai PIN 5 lần thì khoá tới mốc này — chống dò PIN ở quầy."),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employees", x => x.id);
                },
                comment: "Hồ sơ nhân viên — KHÁC tài khoản đăng nhập. Chấm công bằng mã NV + PIN riêng trên máy quầy dùng chung.");

            migrationBuilder.CreateTable(
                name: "work_slots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false, comment: "Giờ bắt đầu theo giờ Việt Nam."),
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
                    table.PrimaryKey("PK_work_slots", x => x.id);
                },
                comment: "Khung ca 4 tiếng. Quản lý đặt tên và giờ bắt đầu; độ dài cố định.");

            migrationBuilder.CreateTable(
                name: "attendances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    employee_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    slot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    slot_start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    slot_end_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    check_in_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    late_minutes = table.Column<int>(type: "integer", nullable: false, comment: "Phút trễ so với giờ bắt đầu ca. 0 nếu đúng giờ."),
                    check_out_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    early_leave_minutes = table.Column<int>(type: "integer", nullable: false),
                    worked_minutes = table.Column<int>(type: "integer", nullable: false, comment: "Phút được tính lương = giao giữa [vào, ra] và khung ca."),
                    hourly_wage = table.Column<int>(type: "integer", nullable: false, comment: "Lương giờ chụp lại lúc chấm vào."),
                    pay = table.Column<int>(type: "integer", nullable: false, comment: "Tiền công lần này, đồng. Tính lúc chấm ra."),
                    cash_shift_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendances", x => x.id);
                    table.ForeignKey(
                        name: "FK_attendances_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Một lần chấm công vào/ra. Giờ ca, lương giờ và tiền công được CHỤP LẠI — đổi giờ ca hay tăng lương sau này không làm đổi bảng công đã chốt.");

            migrationBuilder.CreateIndex(
                name: "ix_attendances_bang_cong",
                table: "attendances",
                columns: new[] { "store_id", "work_date" });

            migrationBuilder.CreateIndex(
                name: "ux_attendances_mot_lan_dang_lam",
                table: "attendances",
                column: "employee_id",
                unique: true,
                filter: "check_out_at IS NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_employees_ma_nv",
                table: "employees",
                columns: new[] { "store_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_slots_store_id_sort_order",
                table: "work_slots",
                columns: new[] { "store_id", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendances");

            migrationBuilder.DropTable(
                name: "work_slots");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropColumn(
                name: "closed_by_employee_id",
                table: "cash_shifts");

            migrationBuilder.DropColumn(
                name: "handed_over_to_employee_id",
                table: "cash_shifts");

            migrationBuilder.DropColumn(
                name: "opened_by_employee_id",
                table: "cash_shifts");
        }
    }
}
