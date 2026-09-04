using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QlyCoffee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SoCheVaThueGTGT : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "reference_type",
                table: "stock_movements",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                comment: "Loại chứng từ nguồn: ORDER | PURCHASE | PREP | COUNT | WASTE | EXPIRY",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true,
                oldComment: "Loại chứng từ nguồn: ORDER | PURCHASE | COUNT | WASTE | EXPIRY");

            migrationBuilder.AddColumn<string>(
                name: "pairing_note",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "modifier_groups",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "prep_recipe_id",
                table: "inventory_lots",
                type: "uuid",
                nullable: true,
                comment: "Công thức sơ chế đã tạo ra lô này. NULL = lô mua từ nhà cung cấp.");

            migrationBuilder.AddColumn<bool>(
                name: "is_prepared",
                table: "ingredients",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "true = BÁN THÀNH PHẨM do quán tự nấu/ủ (cốt trà, cà phê phin, nước đường). Chỉ vào kho qua màn hình Sơ chế, không nhập từ nhà cung cấp.");

            migrationBuilder.CreateTable(
                name: "prep_recipes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false, comment: "Mã công thức, duy nhất trong chi nhánh. VD: PREP-TEA-BLK"),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    output_ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    output_quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false, comment: "Sản lượng MỘT MẺ CHUẨN, SAU hao hụt — lượng thật sự rót vào bình, không phải lượng nước đổ vào nồi."),
                    shelf_life_hours = table.Column<int>(type: "integer", nullable: false, comment: "Hạn dùng của mẻ tính bằng GIỜ. Phải là giờ chứ không phải ngày: cốt trà hỏng sau 6 tiếng, ghi 1 ngày là cho phép bán trà ủ từ sáng vào lúc tối."),
                    prep_minutes = table.Column<int>(type: "integer", nullable: false, comment: "Thời gian làm xong một mẻ, tính bằng phút."),
                    instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Hiện nguyên văn cho nhân viên. Nhiệt độ nước và thời gian ủ nằm ở đây."),
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
                    table.PrimaryKey("PK_prep_recipes", x => x.id);
                    table.ForeignKey(
                        name: "FK_prep_recipes_ingredients_output_ingredient_id",
                        column: x => x.output_ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_prep_recipes_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "CÔNG THỨC MỘT MẺ SƠ CHẾ: 80g lá hồng trà → 2000ml cốt hồng trà, hạn 6 tiếng. Khác recipe_items ở chỗ recipe_items tính cho MỘT LY, bảng này tính cho MỘT MẺ. Chạy một mẻ sinh bút toán ProductionOut cho nguyên liệu thô và ProductionIn kèm một lô mới cho bán thành phẩm.");

            migrationBuilder.CreateTable(
                name: "prep_recipe_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    prep_recipe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<double>(type: "double precision", precision: 18, scale: 4, nullable: false, comment: "Lượng cho MỘT MẺ CHUẨN, theo đơn vị cơ sở của nguyên liệu thô."),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prep_recipe_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_prep_recipe_lines_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_prep_recipe_lines_prep_recipes_prep_recipe_id",
                        column: x => x.prep_recipe_id,
                        principalTable: "prep_recipes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Nguyên liệu thô cần cho MỘT MẺ. Làm hai mẻ thì hệ thống nhân đôi, không sửa số này.");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_lots_prep_recipe_id",
                table: "inventory_lots",
                column: "prep_recipe_id");

            migrationBuilder.CreateIndex(
                name: "IX_prep_recipe_lines_ingredient_id",
                table: "prep_recipe_lines",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_prep_recipe_lines_prep_recipe_id_ingredient_id",
                table: "prep_recipe_lines",
                columns: new[] { "prep_recipe_id", "ingredient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prep_recipes_output_ingredient_id",
                table: "prep_recipes",
                column: "output_ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_prep_recipes_store_id_code",
                table: "prep_recipes",
                columns: new[] { "store_id", "code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_lots_prep_recipes_prep_recipe_id",
                table: "inventory_lots",
                column: "prep_recipe_id",
                principalTable: "prep_recipes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_lots_prep_recipes_prep_recipe_id",
                table: "inventory_lots");

            migrationBuilder.DropTable(
                name: "prep_recipe_lines");

            migrationBuilder.DropTable(
                name: "prep_recipes");

            migrationBuilder.DropIndex(
                name: "IX_inventory_lots_prep_recipe_id",
                table: "inventory_lots");

            migrationBuilder.DropColumn(
                name: "pairing_note",
                table: "products");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "modifier_groups");

            migrationBuilder.DropColumn(
                name: "prep_recipe_id",
                table: "inventory_lots");

            migrationBuilder.DropColumn(
                name: "is_prepared",
                table: "ingredients");

            migrationBuilder.AlterColumn<string>(
                name: "reference_type",
                table: "stock_movements",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                comment: "Loại chứng từ nguồn: ORDER | PURCHASE | COUNT | WASTE | EXPIRY",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true,
                oldComment: "Loại chứng từ nguồn: ORDER | PURCHASE | PREP | COUNT | WASTE | EXPIRY");
        }
    }
}
