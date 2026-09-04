using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QlyCoffee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ThueGtgtVaThucDonMoRong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tax_code",
                table: "stores",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "Mã số thuế in lên hóa đơn.");

            migrationBuilder.AddColumn<int>(
                name: "tax_mode",
                table: "stores",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "0=không tách thuế (hộ nộp trực tiếp) · 1=giá đã gồm thuế (Luật Giá 2023 Đ.29) · 2=giá chưa gồm thuế.");

            migrationBuilder.AddColumn<int>(
                name: "vat_rate_percent",
                table: "stores",
                type: "integer",
                nullable: false,
                defaultValue: 8,
                comment: "Thuế suất GTGT %. 8% theo Nghị quyết 204/2025/QH15, hiệu lực tới 31/12/2026.");

            migrationBuilder.AddColumn<int>(
                name: "serve_style",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "0=đồ uống đá · 1=đồ uống nóng · 2=đồ ăn. Quyết định món có size/mức đá không, và tỷ lệ giá vốn mục tiêu khi gợi ý giá.");

            migrationBuilder.AddColumn<int>(
                name: "net_amount",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Tiền hàng chưa thuế. Luôn thỏa net_amount + tax_amount = grand_total.");

            migrationBuilder.AddColumn<int>(
                name: "paid_amount",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Số tiền thực nhận. Có thể lớn hơn grand_total khi khách chuyển dư.");

            migrationBuilder.AddColumn<DateTime>(
                name: "paid_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "payment_gateway_id",
                table: "orders",
                type: "bigint",
                nullable: true,
                comment: "Id giao dịch SePay. Có giá trị = tiền do ngân hàng xác nhận tự động, không phải nhân viên bấm tay.");

            migrationBuilder.AddColumn<string>(
                name: "payment_ref",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "Nội dung chuyển khoản in trên mã QR, VD MCC7K2M9. Khóa đối soát của webhook SePay.");

            migrationBuilder.AddColumn<int>(
                name: "tax_amount",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Tiền thuế GTGT của đơn.");

            migrationBuilder.AddColumn<int>(
                name: "tax_mode",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Chế độ thuế đã áp dụng cho đơn. Đơn có trước khi hệ thống tách thuế mang giá trị 0.");

            migrationBuilder.AddColumn<int>(
                name: "tax_rate_percent",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Thuế suất % đã áp dụng cho đơn này.");

            // ------------------------------------------------------------------
            //  Đơn hàng CŨ: giữ nguyên bản chất, không bịa thêm thuế
            //
            //  Những đơn này được lập trước khi hệ thống tách thuế GTGT, nên
            //  tax_mode = 0 (không tách thuế) và tax_amount = 0 là ĐÚNG — gán cho
            //  chúng thuế suất 8% là ghi khống số thuế chưa từng thu của khách.
            //
            //  Chỉ cần net_amount = grand_total để bất biến
            //  "net_amount + tax_amount = grand_total" đúng với MỌI dòng, nhờ vậy
            //  báo cáo và màn hình hóa đơn không phải xử lý riêng đơn cũ.
            // ------------------------------------------------------------------
            migrationBuilder.Sql(
                "UPDATE orders SET net_amount = grand_total WHERE net_amount = 0;");

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway_id = table.Column<long>(type: "bigint", nullable: false, comment: "Id giao dịch do SePay cấp. Duy nhất — chống ghi nhận một lần chuyển tiền hai lần."),
                    gateway = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    account_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sub_account = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    transfer_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "in = tiền vào, out = tiền ra. Chỉ 'in' mới được xét thanh toán đơn."),
                    amount = table.Column<int>(type: "integer", nullable: false, comment: "Số tiền giao dịch, đơn vị đồng."),
                    content = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Nội dung chuyển khoản nguyên văn từ ngân hàng."),
                    reference_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    detected_ref = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, comment: "Mã tham chiếu của quán dò ra từ nội dung. null = giao dịch không phải trả đơn."),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    match_status = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true, comment: "Diễn giải tiếng Việt kết quả đối soát — hiện thẳng cho chủ quán đọc."),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: false, comment: "Payload webhook nguyên văn. Dữ liệu tiền bạc do bên thứ ba gửi — luôn giữ bản gốc."),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_transactions", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_transactions_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                },
                comment: "Nhật ký biến động số dư ngân hàng do SePay gửi qua webhook. GIỮ CẢ giao dịch không khớp đơn nào — tiền đã vào tài khoản mà hệ thống im lặng bỏ qua là trường hợp tệ nhất khi đối soát cuối ngày.");

            migrationBuilder.CreateIndex(
                name: "IX_orders_payment_ref",
                table: "orders",
                column: "payment_ref",
                unique: true,
                filter: "payment_ref IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_gateway_id",
                table: "payment_transactions",
                column: "gateway_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_match_status",
                table: "payment_transactions",
                column: "match_status");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_order_id",
                table: "payment_transactions",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_transaction_date",
                table: "payment_transactions",
                column: "transaction_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_transactions");

            migrationBuilder.DropIndex(
                name: "IX_orders_payment_ref",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tax_code",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "tax_mode",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "vat_rate_percent",
                table: "stores");

            migrationBuilder.DropColumn(
                name: "serve_style",
                table: "products");

            migrationBuilder.DropColumn(
                name: "net_amount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "paid_amount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "paid_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment_gateway_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment_ref",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tax_amount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tax_mode",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tax_rate_percent",
                table: "orders");
        }
    }
}
