using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QlyCoffee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DaNhapBuNguyenLieu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "resolved_at",
                table: "stock_shortage_logs",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Lúc đã nhập bù đủ. NULL = còn treo, danh sách \"Cần nhập hàng\" vẫn báo.");

            migrationBuilder.AddColumn<double>(
                name: "restocked_quantity",
                table: "stock_shortage_logs",
                type: "double precision",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0.0,
                comment: "Tổng lượng đã nhập bù kể từ lần chặn đầu trong ngày.");

            migrationBuilder.CreateIndex(
                name: "IX_stock_shortage_logs_store_id_business_date",
                table: "stock_shortage_logs",
                columns: new[] { "store_id", "business_date" },
                filter: "resolved_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stock_shortage_logs_store_id_business_date",
                table: "stock_shortage_logs");

            migrationBuilder.DropColumn(
                name: "resolved_at",
                table: "stock_shortage_logs");

            migrationBuilder.DropColumn(
                name: "restocked_quantity",
                table: "stock_shortage_logs");
        }
    }
}
