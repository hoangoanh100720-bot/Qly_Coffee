using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QlyCoffee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CaLamViecVaDoiSoatKet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_shifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    opened_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_by_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    opening_cash = table.Column<int>(type: "integer", nullable: false, comment: "Tiền mặt đếm được trong két lúc nhận ca, đồng."),
                    expected_opening_cash = table.Column<int>(type: "integer", nullable: true, comment: "Số ca trước bàn giao lại. Khác opening_cash = két lệch giữa hai ca."),
                    received_from_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    opening_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    closed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_by_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    handed_over_to_id = table.Column<Guid>(type: "uuid", nullable: true),
                    handed_over_to_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    counted_cash = table.Column<int>(type: "integer", nullable: true, comment: "Tiền mặt thực đếm lúc đóng ca, đồng."),
                    handover_cash = table.Column<int>(type: "integer", nullable: true, comment: "Để lại trong két cho ca sau, đồng."),
                    deposited_cash = table.Column<int>(type: "integer", nullable: true, comment: "Rút ra nộp quản lý = counted_cash − handover_cash."),
                    closing_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cash_sales = table.Column<int>(type: "integer", nullable: true),
                    transfer_sales = table.Column<int>(type: "integer", nullable: true),
                    paid_order_count = table.Column<int>(type: "integer", nullable: true),
                    expected_cash = table.Column<int>(type: "integer", nullable: true, comment: "Tiền mặt lẽ ra có trong két = opening_cash + cash_sales."),
                    cash_difference = table.Column<int>(type: "integer", nullable: true, comment: "counted_cash − expected_cash. Âm là hụt két."),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_shifts", x => x.id);
                },
                comment: "Một ca đứng két: ai nhận, nhận bao nhiêu, thu được bao nhiêu, đếm lại còn bao nhiêu, giao cho ai. Số liệu chụp lại lúc đóng ca, không tính lại khi đọc.");

            migrationBuilder.CreateIndex(
                name: "ix_cash_shifts_bao_cao",
                table: "cash_shifts",
                columns: new[] { "store_id", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ux_cash_shifts_mot_ca_dang_mo",
                table: "cash_shifts",
                column: "store_id",
                unique: true,
                filter: "status = 0 AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_shifts");
        }
    }
}
