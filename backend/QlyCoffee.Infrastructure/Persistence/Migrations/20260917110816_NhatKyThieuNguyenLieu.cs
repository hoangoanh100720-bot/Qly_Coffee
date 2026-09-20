using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QlyCoffee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NhatKyThieuNguyenLieu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_shortage_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    max_missing_quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false, comment: "Lượng thiếu LỚN NHẤT của một lần bị chặn, không phải tổng — bấm lại nhiều lần không được thổi phồng số cần nhập."),
                    blocked_count = table.Column<int>(type: "integer", nullable: false, comment: "Số lần bấm nhận đơn bị chặn vì nguyên liệu này trong ngày."),
                    affected_products = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    last_blocked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_shortage_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_shortage_logs_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Nguyên liệu đã chặn việc nhận đơn, gộp theo ngày. Nguồn của danh sách 'Cần nhập hàng' trên trang Kế hoạch — tồn kho cuối ngày không cho biết trong ngày đã phải từ chối bao nhiêu đơn vì thiếu thứ gì.");

            migrationBuilder.CreateIndex(
                name: "IX_stock_shortage_logs_ingredient_id",
                table: "stock_shortage_logs",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_shortage_logs_store_id_business_date_ingredient_id",
                table: "stock_shortage_logs",
                columns: new[] { "store_id", "business_date", "ingredient_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_shortage_logs");
        }
    }
}
