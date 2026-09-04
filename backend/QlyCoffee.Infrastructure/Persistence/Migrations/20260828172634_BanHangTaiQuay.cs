using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QlyCoffee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BanHangTaiQuay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "orders",
                comment: "Đơn hàng. KHO ĐƯỢC TRỪ khi chuyển sang Completed(4) — tức là lúc nhân viên bấm Hoàn tất sau khi pha xong — và HOÀN LẠI khi Cancelled(5). Không trạng thái nào khác động vào kho. Confirmed(1) và Preparing(2) chỉ đưa đơn vào hàng pha.",
                oldComment: "Đơn hàng. KHO ĐƯỢC TRỪ khi chuyển sang Confirmed(1) và HOÀN LẠI khi Cancelled(5). Không trạng thái nào khác động vào kho.");

            migrationBuilder.AddColumn<int>(
                name: "prep_seconds",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Giây để pha xong 1 ly. Là con số duy nhất quyết định thời gian báo khách.");

            migrationBuilder.AddColumn<int>(
                name: "channel",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "0=khách đặt online, 1=nhân viên bấm tại quầy.");

            migrationBuilder.AddColumn<DateTime>(
                name: "estimated_ready_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Thời gian ĐÃ HỨA với khách. Không tính lại — dùng để đối chiếu hứa/thực.");

            migrationBuilder.CreateIndex(
                name: "ix_orders_hang_pha",
                table: "orders",
                columns: new[] { "store_id", "status", "confirmed_at" });

            // ---- Điền thời gian pha cho các món ĐÃ CÓ SẴN ---------------------
            // Thêm cột với mặc định 0 rồi bỏ đó là hỏng: thời gian báo khách sẽ
            // luôn ra 1 phút bất kể gọi bao nhiêu ly. Cột này không có giá trị
            // mặc định nào đúng cho mọi món, nên phải điền theo nhóm.
            //
            // Đây là số KHỞI ĐẦU để hệ thống chạy được ngay. Quán phải bấm đồng
            // hồ đo lại theo tay nghề nhân viên mình — màn hình pha chế có báo
            // "xong sớm/trễ N phút" sau mỗi đơn chính là để hiệu chỉnh con số này.
            migrationBuilder.Sql("""
                UPDATE products p SET prep_seconds = CASE c.slug
                    WHEN 'ca-phe'       THEN 100   -- pha máy hoặc phin đã ủ sẵn
                    WHEN 'tra-sua'      THEN 120   -- ủ trà, lắc, múc trân châu
                    WHEN 'tra-trai-cay' THEN 130   -- dằm trái cây tươi tại chỗ
                    WHEN 'da-xay'       THEN 150   -- xay rồi còn phải tráng cối
                    WHEN 'matcha-cacao' THEN 100   -- đánh matcha bằng chasen
                    ELSE 90
                END
                FROM categories c
                WHERE c.id = p.category_id AND p.prep_seconds = 0;
                """);

            // Ba món lệch hẳn mặt bằng nhóm của chúng
            migrationBuilder.Sql("""
                UPDATE products SET prep_seconds = 210 WHERE slug = 'ca-phe-kem-trung';
                UPDATE products SET prep_seconds = 140 WHERE slug = 'ca-phe-muoi';
                UPDATE products SET prep_seconds = 45  WHERE slug = 'cold-brew';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_hang_pha",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "prep_seconds",
                table: "products");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "estimated_ready_at",
                table: "orders");

            migrationBuilder.AlterTable(
                name: "orders",
                comment: "Đơn hàng. KHO ĐƯỢC TRỪ khi chuyển sang Confirmed(1) và HOÀN LẠI khi Cancelled(5). Không trạng thái nào khác động vào kho.",
                oldComment: "Đơn hàng. KHO ĐƯỢC TRỪ khi chuyển sang Completed(4) — tức là lúc nhân viên bấm Hoàn tất sau khi pha xong — và HOÀN LẠI khi Cancelled(5). Không trạng thái nào khác động vào kho. Confirmed(1) và Preparing(2) chỉ đưa đơn vào hàng pha.");
        }
    }
}
