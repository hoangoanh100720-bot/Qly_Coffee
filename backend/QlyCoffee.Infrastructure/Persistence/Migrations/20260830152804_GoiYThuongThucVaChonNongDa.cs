using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QlyCoffee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GoiYThuongThucVaChonNongDa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "modifier_groups",
                comment: "Nhóm tùy chọn: Topping, Mức đường, Dùng nóng hay đá, Mức đá.",
                oldComment: "Nhóm tùy chọn: Topping, Mức đường, Mức đá.");

            migrationBuilder.AlterColumn<string>(
                name: "pairing_note",
                table: "products",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                comment: "Gợi ý thưởng thức: dùng nóng/lạnh thế nào, ăn kèm món nào. Rỗng thì giao diện ẩn khối này.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "modifier_groups",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "",
                comment: "topping · sugar · ice · temperature · rỗng = nhóm thường. Giao diện đọc cột này để biết nhóm nào phải ẩn khi khách chọn dùng nóng — KHÔNG so theo tên nhóm.",
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "modifier_groups",
                comment: "Nhóm tùy chọn: Topping, Mức đường, Mức đá.",
                oldComment: "Nhóm tùy chọn: Topping, Mức đường, Dùng nóng hay đá, Mức đá.");

            migrationBuilder.AlterColumn<string>(
                name: "pairing_note",
                table: "products",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true,
                oldComment: "Gợi ý thưởng thức: dùng nóng/lạnh thế nào, ăn kèm món nào. Rỗng thì giao diện ẩn khối này.");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "modifier_groups",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "",
                oldComment: "topping · sugar · ice · temperature · rỗng = nhóm thường. Giao diện đọc cột này để biết nhóm nào phải ẩn khi khách chọn dùng nóng — KHÔNG so theo tên nhóm.");
        }
    }
}
