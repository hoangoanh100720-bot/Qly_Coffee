using Microsoft.EntityFrameworkCore;
using QlyCoffee.Domain.Entities;

namespace QlyCoffee.Infrastructure.Persistence;

// ==============================================================================
//  DBCONTEXT — CẤU HÌNH ÁNH XẠ THỰC THỂ SANG BẢNG POSTGRESQL
//
//  Nguyên tắc cấu hình trong file này:
//
//  1. TIỀN luôn là int (đồng Việt Nam, không thập phân). Không dùng decimal/float.
//  2. SỐ LƯỢNG nguyên liệu là double với độ chính xác cao (numeric(18,4)).
//  3. Mọi bảng có DeletedAt đều bật QUERY FILTER tự động loại bản ghi đã xóa.
//  4. Chỉ số (index) được đặt theo TRUY VẤN THỰC TẾ, không đặt bừa —
//     index thừa làm chậm ghi và tốn dung lượng.
//  5. Mọi bảng và cột quan trọng đều có COMMENT trong database, để người dùng
//     pgAdmin/DBeaver đọc được ý nghĩa mà không cần mở code.
// ==============================================================================

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // --- Tài khoản --------------------------------------------------------------
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // --- Danh mục & món ---------------------------------------------------------
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<RecipeItem> RecipeItems => Set<RecipeItem>();
    public DbSet<ModifierGroup> ModifierGroups => Set<ModifierGroup>();
    public DbSet<Modifier> Modifiers => Set<Modifier>();
    public DbSet<ModifierRecipeItem> ModifierRecipeItems => Set<ModifierRecipeItem>();
    public DbSet<ProductModifier> ProductModifiers => Set<ProductModifier>();

    // --- Kho --------------------------------------------------------------------
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<PurchaseUnit> PurchaseUnits => Set<PurchaseUnit>();
    public DbSet<InventoryLot> InventoryLots => Set<InventoryLot>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();

    // --- Sơ chế (nguyên liệu thô → bán thành phẩm) -------------------------------
    public DbSet<PrepRecipe> PrepRecipes => Set<PrepRecipe>();
    public DbSet<PrepRecipeLine> PrepRecipeLines => Set<PrepRecipeLine>();

    // --- Đơn hàng ---------------------------------------------------------------
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    // --- Thanh toán ---------------------------------------------------------------
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    // --- Khuyến mãi & kế hoạch AI ----------------------------------------------
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<DailyPlan> DailyPlans => Set<DailyPlan>();
    public DbSet<PlanSuggestion> PlanSuggestions => Set<PlanSuggestion>();
    public DbSet<PlanDecision> PlanDecisions => Set<PlanDecision>();
    public DbSet<DailyConsumption> DailyConsumptions => Set<DailyConsumption>();
    public DbSet<DailySales> DailySales => Set<DailySales>();
    public DbSet<StockShortageLog> StockShortageLogs => Set<StockShortageLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ======================================================================
        //  TÀI KHOẢN
        // ======================================================================
        b.Entity<User>(e =>
        {
            e.ToTable("users", t => t.HasComment(
                "Tài khoản người dùng: khách hàng và nhân sự của quán."));

            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Phone).IsUnique().HasFilter("phone IS NOT NULL");

            e.Property(x => x.Email).HasMaxLength(256).IsRequired()
                .HasComment("Email đăng nhập, luôn lưu chữ thường, duy nhất toàn hệ thống.");
            e.Property(x => x.Phone).HasMaxLength(20)
                .HasComment("Số điện thoại VN. Dữ liệu cá nhân — phải xóa được theo yêu cầu (NĐ 13/2023).");
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(255)
                .HasComment("Băm bằng BCrypt work factor 12. Không bao giờ lưu mật khẩu gốc.");
            e.Property(x => x.Role)
                .HasComment("0=Customer 1=Staff 2=Manager 3=Owner. Quyết định toàn bộ quyền truy cập.");

            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens", t => t.HasComment(
                "Token làm mới phiên đăng nhập. Xoay vòng mỗi lần dùng để chống đánh cắp."));
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.Token).HasMaxLength(512).IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ======================================================================
        //  CỬA HÀNG & DANH MỤC
        // ======================================================================
        b.Entity<Store>(e =>
        {
            e.ToTable("stores", t => t.HasComment(
                "Chi nhánh. MVP chỉ có một bản ghi, nhưng thiết kế sẵn cho nhiều chi nhánh."));
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(150).IsRequired();
            e.Property(x => x.Address).HasMaxLength(300).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            e.Property(x => x.OpenTime).HasMaxLength(5).HasComment("Định dạng HH:mm");
            e.Property(x => x.CloseTime).HasMaxLength(5).HasComment("Định dạng HH:mm");
            e.Property(x => x.TimeZone).HasMaxLength(64)
                .HasComment("Múi giờ IANA. Mọi phép tính theo ngày kinh doanh phải dùng giá trị này.");
            e.Property(x => x.BusinessDayEndHour)
                .HasComment("Giờ chốt sổ (0-23). Job sinh kế hoạch chạy sau mốc này.");

            // --- Thuế GTGT — căn cứ pháp lý xem QlyCoffee.Shared/Tax.cs -------
            e.Property(x => x.TaxMode)
                .HasDefaultValue(1)
                .HasComment("0=không tách thuế (hộ nộp trực tiếp) · 1=giá đã gồm thuế (Luật Giá 2023 Đ.29) · 2=giá chưa gồm thuế.");
            e.Property(x => x.VatRatePercent)
                .HasDefaultValue(8)
                .HasComment("Thuế suất GTGT %. 8% theo Nghị quyết 204/2025/QH15, hiệu lực tới 31/12/2026.");
            e.Property(x => x.TaxCode).HasMaxLength(20)
                .HasComment("Mã số thuế in lên hóa đơn.");

            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<Category>(e =>
        {
            e.ToTable("categories", t => t.HasComment("Danh mục món trên menu."));
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.IsActive, x.SortOrder });
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            e.Property(x => x.ColorHex).HasMaxLength(9)
                .HasComment("Màu đại diện danh mục, hex #RRGGBB. Lấy từ màu đặc trưng của nhóm đồ uống.");
            e.Property(x => x.IconKey).HasMaxLength(40)
                .HasComment("Mã icon SVG dựng sẵn ở frontend, không dùng file ảnh ngoài.");
            e.HasOne(x => x.Store).WithMany(s => s.Categories)
                .HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        // ======================================================================
        //  MÓN & CÔNG THỨC ĐỊNH LƯỢNG
        // ======================================================================
        b.Entity<Product>(e =>
        {
            e.ToTable("products", t => t.HasComment(
                "Món trên menu. Giá vốn (computed_cost) là số DẪN XUẤT từ công thức, không nhập tay."));

            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.CategoryId, x.IsActive });
            // Index phục vụ trang menu: lọc món còn bán được
            e.HasIndex(x => new { x.StoreId, x.IsAvailable, x.SortOrder });

            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(150).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.PairingNote).HasMaxLength(300)
                .HasComment("Gợi ý thưởng thức: dùng nóng/lạnh thế nào, ăn kèm món nào. Rỗng thì giao diện ẩn khối này.");

            e.Property(x => x.ColorPrimaryHex).HasMaxLength(9)
                .HasComment("Màu THẬT của ly nước, hex. Dùng dựng minh họa SVG khi chưa có ảnh chụp.");
            e.Property(x => x.ColorAccentHex).HasMaxLength(9)
                .HasComment("Màu lớp kem/lớp trên. Kết hợp với màu chính tạo hình ly hai lớp giống thật.");

            e.Property(x => x.BasePrice)
                .HasComment("Giá bán size mặc định, đơn vị ĐỒNG (int, không thập phân).");
            e.Property(x => x.ComputedCost)
                .HasComment("Giá vốn tính từ công thức: Σ(lượng × giá vốn TB nguyên liệu × (1+hao hụt)).");
            e.Property(x => x.MaxServings)
                .HasComment("Số ly tối đa còn làm được = min(tồn kho / định mức) trên nguyên liệu bắt buộc.");
            e.Property(x => x.UnavailableReason).HasMaxLength(200);
            e.Property(x => x.Tags).HasMaxLength(300)
                .HasComment("Nhãn ngăn cách bởi dấu phẩy. VD: best-seller,mới");
            e.Property(x => x.PrepSeconds)
                .HasComment("Giây để pha xong 1 ly. Là con số duy nhất quyết định thời gian báo khách.");
            e.Property(x => x.ServeStyle)
                .HasComment("0=đồ uống đá · 1=đồ uống nóng · 2=đồ ăn. Quyết định món có size/mức đá không, và tỷ lệ giá vốn mục tiêu khi gợi ý giá.");

            e.Ignore(x => x.MarginPercent);   // thuộc tính tính toán, không lưu DB

            e.HasOne(x => x.Category).WithMany(c => c.Products)
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<ProductVariant>(e =>
        {
            e.ToTable("product_variants", t => t.HasComment(
                "Biến thể món (size). recipe_multiplier CHỈ nhân công thức gốc, KHÔNG nhân topping."));
            e.HasIndex(x => x.ProductId);
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.RecipeMultiplier).HasPrecision(6, 3)
                .HasComment("Hệ số nhân công thức gốc. Size L thường 1.4. KHÔNG áp dụng cho topping.");
            e.HasOne(x => x.Product).WithMany(p => p.Variants)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RecipeItem>(e =>
        {
            e.ToTable("recipe_items", t => t.HasComment(
                "CÔNG THỨC ĐỊNH LƯỢNG — bảng nối giữa MENU và KHO. " +
                "Không có bảng này thì không thể tự trừ tồn kho, không tính được giá vốn."));

            // Một nguyên liệu chỉ xuất hiện một lần trong công thức của một món
            e.HasIndex(x => new { x.ProductId, x.IngredientId }).IsUnique();
            e.HasIndex(x => x.IngredientId)
                .HasDatabaseName("ix_recipe_items_ingredient")
                ; // phục vụ truy vấn ngược: nguyên liệu này dùng cho những món nào

            e.Property(x => x.Quantity).HasPrecision(18, 4)
                .HasComment("Lượng cho MỘT ly size mặc định, theo đơn vị cơ sở của nguyên liệu.");
            e.Property(x => x.IsOptional)
                .HasComment("Nguyên liệu tùy chọn KHÔNG tính vào phép kiểm tra còn làm được bao nhiêu ly.");
            e.Property(x => x.Note).HasMaxLength(300);

            e.HasOne(x => x.Product).WithMany(p => p.RecipeItems)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Ingredient).WithMany(i => i.RecipeItems)
                .HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ModifierGroup>(e =>
        {
            e.ToTable("modifier_groups", t => t.HasComment(
                "Nhóm tùy chọn: Topping, Mức đường, Dùng nóng hay đá, Mức đá."));
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Kind).HasMaxLength(24).HasDefaultValue("").HasComment(
                "topping · sugar · ice · temperature · rỗng = nhóm thường. Giao diện đọc cột này "
                + "để biết nhóm nào phải ẩn khi khách chọn dùng nóng — KHÔNG so theo tên nhóm.");
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<Modifier>(e =>
        {
            e.ToTable("modifiers", t => t.HasComment("Một lựa chọn cụ thể trong nhóm tùy chọn."));
            e.HasIndex(x => x.ModifierGroupId);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ColorHex).HasMaxLength(9);
            e.HasOne(x => x.Group).WithMany(g => g.Modifiers)
                .HasForeignKey(x => x.ModifierGroupId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ModifierRecipeItem>(e =>
        {
            e.ToTable("modifier_recipe_items", t => t.HasComment(
                "Công thức riêng của topping. VD: Trân châu đen → trừ thêm 40g trân châu."));
            e.HasIndex(x => new { x.ModifierId, x.IngredientId }).IsUnique();
            e.Property(x => x.Quantity).HasPrecision(18, 4)
                .HasComment("KHÔNG nhân hệ số size — thêm topping vào size L vẫn cùng lượng.");
            e.HasOne(x => x.Modifier).WithMany(m => m.RecipeItems)
                .HasForeignKey(x => x.ModifierId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Ingredient).WithMany()
                .HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ProductModifier>(e =>
        {
            e.ToTable("product_modifiers", t => t.HasComment("Bảng nối: món nào có nhóm tùy chọn nào."));
            e.HasKey(x => new { x.ProductId, x.ModifierGroupId });
            e.HasOne(x => x.Product).WithMany(p => p.ModifierLinks)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Group).WithMany(g => g.ProductLinks)
                .HasForeignKey(x => x.ModifierGroupId).OnDelete(DeleteBehavior.Cascade);
        });

        // ======================================================================
        //  KHO
        // ======================================================================
        b.Entity<Ingredient>(e =>
        {
            e.ToTable("ingredients", t => t.HasComment(
                "ĐỊNH NGHĨA nguyên liệu. Không chứa số lượng — số lượng nằm ở inventory_lots " +
                "vì mỗi lô có hạn dùng và giá vốn riêng."));

            e.HasIndex(x => x.Sku).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.Category });
            e.HasIndex(x => new { x.StoreId, x.IsActive });

            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Sku).HasMaxLength(60).IsRequired()
                .HasComment("Mã nội bộ [NHÓM]-[TÊN]-[SỐ]. VD: DAI-MILK-01");
            e.Property(x => x.BaseUnit)
                .HasComment("0=Gram 1=Milliliter 2=Piece. MỌI số lượng của nguyên liệu này theo đơn vị đây.");
            e.Property(x => x.ColorHex).HasMaxLength(9)
                .HasComment("Màu THẬT của nguyên liệu. Là DỮ LIỆU: dùng vẽ minh họa SVG và chấm màu bảng kho.");
            e.Property(x => x.IconKey).HasMaxLength(40)
                .HasComment("Mã hình SVG dựng sẵn ở frontend: milk, coffee-beans, boba, peach...");
            e.Property(x => x.MinStockLevel).HasPrecision(18, 4)
                .HasComment("Ngưỡng cảnh báo đỏ, theo đơn vị cơ sở.");
            e.Property(x => x.ReorderPoint).HasPrecision(18, 4)
                .HasComment("Điểm đặt hàng lại = tồn tối thiểu + (tiêu thụ TB/ngày × số ngày giao hàng NCC).");
            e.Property(x => x.DefaultShelfLifeDays)
                .HasComment("Số ngày HSD mặc định kể từ ngày nhập. NULL = không có hạn (đá, ly nhựa).");
            e.Property(x => x.ExpiryWarningDays)
                .HasComment("Còn bao nhiêu ngày thì coi là cận hạn. Trái cây tươi để 2-3, đồ khô để 14-30.");
            e.Property(x => x.WastageRate).HasPrecision(6, 4)
                .HasComment("Hao hụt chế biến. 0.03 = 3%. Trái cây gọt vỏ 0.10-0.15, bột/siro 0.01.");
            e.Property(x => x.AverageUnitCost)
                .HasComment("Giá vốn bình quân gia quyền, ĐỒNG cho 1 đơn vị cơ sở. Tự cập nhật khi nhập kho.");
            e.Property(x => x.IsPrepared)
                .HasComment("true = BÁN THÀNH PHẨM do quán tự nấu/ủ (cốt trà, cà phê phin, nước đường). " +
                            "Chỉ vào kho qua màn hình Sơ chế, không nhập từ nhà cung cấp.");
            e.Property(x => x.Note).HasMaxLength(500);

            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<PurchaseUnit>(e =>
        {
            e.ToTable("purchase_units", t => t.HasComment(
                "Đơn vị mua hàng và hệ số quy đổi sang đơn vị cơ sở. VD: Thùng 12 hộp 1L = 12000 ml."));
            e.HasIndex(x => x.IngredientId);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ConversionQuantity).HasPrecision(18, 4)
                .HasComment("Số đơn vị cơ sở trong MỘT đơn vị mua.");
            e.HasOne(x => x.Ingredient).WithMany(i => i.PurchaseUnits)
                .HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<InventoryLot>(e =>
        {
            e.ToTable("inventory_lots", t => t.HasComment(
                "MỘT LÔ HÀNG cụ thể. Phải tách lô vì mỗi lần nhập có HSD và giá khác nhau — " +
                "đây là nền tảng của FEFO và của toàn bộ tính năng cảnh báo cận hạn."));

            e.HasIndex(x => new { x.StoreId, x.LotCode }).IsUnique();

            // ⭐ INDEX QUAN TRỌNG NHẤT CỦA CẢ SCHEMA
            // Phục vụ truy vấn FEFO: lấy lô còn hàng của một nguyên liệu, sắp theo HSD tăng dần.
            // Thiếu index này thì mỗi lần bán hàng phải quét toàn bảng.
            e.HasIndex(x => new { x.StoreId, x.IngredientId, x.Status, x.ExpiryDate })
                .HasDatabaseName("ix_lots_fefo");

            // Phục vụ màn hình cảnh báo cận hạn và job phân tích rủi ro
            e.HasIndex(x => new { x.StoreId, x.ExpiryDate, x.Status })
                .HasDatabaseName("ix_lots_expiry_watch");

            e.Property(x => x.LotCode).HasMaxLength(80).IsRequired()
                .HasComment("Mã lô [SKU]-[YYMMDD]-[chữ cái]. Tự sinh nếu không nhập.");
            e.Property(x => x.ReceivedQuantity).HasPrecision(18, 4);
            e.Property(x => x.RemainingQuantity).HasPrecision(18, 4)
                .HasComment("CHỈ được sửa trong transaction có SELECT...FOR UPDATE, nếu không sẽ tồn kho âm.");
            e.Property(x => x.UnitCost)
                .HasComment("Giá vốn LỊCH SỬ của lô này (đồng/đơn vị cơ sở). Không đổi theo thời gian.");
            e.Property(x => x.ExpiryDate)
                .HasComment("Trường quan trọng nhất bảng: quyết định thứ tự FEFO và cảnh báo lãng phí.");
            e.Property(x => x.Status)
                .HasComment("0=Active 1=Depleted 2=Expired 3=Disposed. Chỉ lô Active mới được FEFO lấy ra.");
            e.Property(x => x.Note).HasMaxLength(300);

            e.Property(x => x.PrepRecipeId)
                .HasComment("Công thức sơ chế đã tạo ra lô này. NULL = lô mua từ nhà cung cấp.");

            e.Ignore(x => x.DaysUntilExpiry);
            e.Ignore(x => x.RemainingValue);

            e.HasOne(x => x.Ingredient).WithMany(i => i.Lots)
                .HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PrepRecipe).WithMany()
                .HasForeignKey(x => x.PrepRecipeId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<StockMovement>(e =>
        {
            e.ToTable("stock_movements", t => t.HasComment(
                "SỔ CÁI KHO — CHỈ GHI THÊM. Không bao giờ UPDATE hay DELETE. " +
                "Ghi sai thì ghi bút toán đảo, giống nguyên tắc kế toán kép. " +
                "Cộng dồn quantity_delta phải khớp tồn kho thực tế."));

            e.HasIndex(x => new { x.StoreId, x.IngredientId, x.OccurredAt });
            e.HasIndex(x => new { x.StoreId, x.Type, x.OccurredAt });
            e.HasIndex(x => new { x.ReferenceType, x.ReferenceId })
                .HasDatabaseName("ix_movements_reference");

            // ⭐ Khóa chống ghi trùng — nền tảng của tính idempotent khi retry
            e.HasIndex(x => x.IdempotencyKey).IsUnique()
                .HasFilter("idempotency_key IS NOT NULL")
                .HasDatabaseName("ux_movements_idempotency");

            e.Property(x => x.QuantityDelta).HasPrecision(18, 4)
                .HasComment("DƯƠNG = nhập kho, ÂM = xuất kho. Không bao giờ bằng 0.");
            e.Property(x => x.Type)
                .HasComment("0=PurchaseIn 1=SaleOut 2=Waste 3=ExpiredOut 4=AdjustIn 5=AdjustOut " +
                            "6=ReturnIn 7=ProductionOut 8=ProductionIn");
            e.Property(x => x.ReferenceType).HasMaxLength(30)
                .HasComment("Loại chứng từ nguồn: ORDER | PURCHASE | PREP | COUNT | WASTE | EXPIRY");
            e.Property(x => x.Reason).HasMaxLength(300)
                .HasComment("BẮT BUỘC với bút toán hao hụt và điều chỉnh kiểm kê.");
            e.Property(x => x.IdempotencyKey).HasMaxLength(200)
                .HasComment("Quy ước: ORDER:{orderId}:ING:{ingredientId}:LOT:{lotId}");

            e.HasOne(x => x.Ingredient).WithMany(i => i.Movements)
                .HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Lot).WithMany(l => l.Movements)
                .HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Supplier>(e =>
        {
            e.ToTable("suppliers", t => t.HasComment("Nhà cung cấp nguyên liệu."));
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.LeadTimeDays)
                .HasComment("Số ngày từ lúc đặt tới lúc nhận. Dùng tính điểm đặt hàng lại.");
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("purchase_orders", t => t.HasComment("Phiếu đặt mua gửi nhà cung cấp."));
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.IsAiGenerated)
                .HasComment("TRUE nếu do AI sinh từ đề xuất Restock. Dùng đo hiệu quả gợi ý nhập hàng.");
            e.HasOne(x => x.Supplier).WithMany(s => s.PurchaseOrders)
                .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<PurchaseOrderItem>(e =>
        {
            e.ToTable("purchase_order_items");
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.ReceivedQuantity).HasPrecision(18, 4)
                .HasComment("Số nhận thực tế. Lệch so với quantity nghĩa là NCC giao thiếu.");
            e.HasOne(x => x.PurchaseOrder).WithMany(p => p.Items)
                .HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StockCount>(e =>
        {
            e.ToTable("stock_counts", t => t.HasComment(
                "Phiếu kiểm kê. Chênh lệch sinh bút toán AdjustIn/AdjustOut."));
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.IsFinalized)
                .HasComment("Đã chốt sổ thì khóa vĩnh viễn và đã sinh bút toán điều chỉnh.");
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<StockCountLine>(e =>
        {
            e.ToTable("stock_count_lines");
            e.Property(x => x.SystemQuantity).HasPrecision(18, 4);
            e.Property(x => x.CountedQuantity).HasPrecision(18, 4);
            e.Property(x => x.VarianceQuantity).HasPrecision(18, 4)
                .HasComment("= counted − system. ÂM nghĩa là thiếu hụt (có thể do hao hụt hoặc thất thoát).");
            e.HasOne(x => x.StockCount).WithMany(s => s.Lines)
                .HasForeignKey(x => x.StockCountId).OnDelete(DeleteBehavior.Cascade);
        });

        // ======================================================================
        //  SƠ CHẾ — NGUYÊN LIỆU THÔ THÀNH BÁN THÀNH PHẨM
        // ======================================================================
        b.Entity<PrepRecipe>(e =>
        {
            e.ToTable("prep_recipes", t => t.HasComment(
                "CÔNG THỨC MỘT MẺ SƠ CHẾ: 80g lá hồng trà → 2000ml cốt hồng trà, hạn 6 tiếng. " +
                "Khác recipe_items ở chỗ recipe_items tính cho MỘT LY, bảng này tính cho MỘT MẺ. " +
                "Chạy một mẻ sinh bút toán ProductionOut cho nguyên liệu thô và ProductionIn " +
                "kèm một lô mới cho bán thành phẩm."));

            e.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
            e.HasIndex(x => x.OutputIngredientId);

            e.Property(x => x.Code).HasMaxLength(60).IsRequired()
                .HasComment("Mã công thức, duy nhất trong chi nhánh. VD: PREP-TEA-BLK");
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.OutputQuantity).HasPrecision(18, 4)
                .HasComment("Sản lượng MỘT MẺ CHUẨN, SAU hao hụt — lượng thật sự rót vào bình, " +
                            "không phải lượng nước đổ vào nồi.");
            e.Property(x => x.ShelfLifeHours)
                .HasComment("Hạn dùng của mẻ tính bằng GIỜ. Phải là giờ chứ không phải ngày: " +
                            "cốt trà hỏng sau 6 tiếng, ghi 1 ngày là cho phép bán trà ủ từ sáng vào lúc tối.");
            e.Property(x => x.PrepMinutes)
                .HasComment("Thời gian làm xong một mẻ, tính bằng phút.");
            e.Property(x => x.Instructions).HasMaxLength(1000)
                .HasComment("Hiện nguyên văn cho nhân viên. Nhiệt độ nước và thời gian ủ nằm ở đây.");

            e.HasOne(x => x.OutputIngredient).WithMany()
                .HasForeignKey(x => x.OutputIngredientId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<PrepRecipeLine>(e =>
        {
            e.ToTable("prep_recipe_lines", t => t.HasComment(
                "Nguyên liệu thô cần cho MỘT MẺ. Làm hai mẻ thì hệ thống nhân đôi, không sửa số này."));

            e.HasIndex(x => new { x.PrepRecipeId, x.IngredientId }).IsUnique();

            e.Property(x => x.Quantity).HasPrecision(18, 4)
                .HasComment("Lượng cho MỘT MẺ CHUẨN, theo đơn vị cơ sở của nguyên liệu thô.");
            e.Property(x => x.Note).HasMaxLength(300);

            e.HasOne(x => x.PrepRecipe).WithMany(p => p.Lines)
                .HasForeignKey(x => x.PrepRecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Ingredient).WithMany()
                .HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        });

        // ======================================================================
        //  ĐƠN HÀNG
        // ======================================================================
        b.Entity<Order>(e =>
        {
            e.ToTable("orders", t => t.HasComment(
                "Đơn hàng. KHO ĐƯỢC TRỪ khi chuyển sang Completed(4) — tức là lúc nhân viên bấm " +
                "Hoàn tất sau khi pha xong — và HOÀN LẠI khi Cancelled(5). Không trạng thái nào " +
                "khác động vào kho. Confirmed(1) và Preparing(2) chỉ đưa đơn vào hàng pha."));

            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.Status, x.PlacedAt });
            e.HasIndex(x => new { x.StoreId, x.PlacedAt });
            e.HasIndex(x => x.CustomerPhone);

            // Màn hình pha chế đọc liên tục 15 giây một lần: lọc theo cửa hàng và
            // trạng thái đang chờ, sắp theo thứ tự vào hàng. Không có index này thì
            // mỗi lần làm mới là một lần quét toàn bảng orders.
            e.HasIndex(x => new { x.StoreId, x.Status, x.ConfirmedAt })
                .HasDatabaseName("ix_orders_hang_pha");

            e.Property(x => x.Channel)
                .HasComment("0=khách đặt online, 1=nhân viên bấm tại quầy.");
            e.Property(x => x.EstimatedReadyAt)
                .HasComment("Thời gian ĐÃ HỨA với khách. Không tính lại — dùng để đối chiếu hứa/thực.");

            e.Property(x => x.Code).HasMaxLength(30).IsRequired()
                .HasComment("Mã đơn QC-YYMMDD-NNNN. Khách tra cứu đơn bằng mã này, không cần đăng nhập.");
            e.Property(x => x.CustomerName).HasMaxLength(150).IsRequired();
            e.Property(x => x.CustomerPhone).HasMaxLength(20).IsRequired()
                .HasComment("Dữ liệu cá nhân — phải xóa được theo yêu cầu (NĐ 13/2023).");
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.CancelReason).HasMaxLength(300);

            e.Property(x => x.CostTotal)
                .HasComment("Giá vốn THỰC TẾ từ đúng những lô đã bị trừ, không phải giá vốn bình quân.");

            // --- Thuế GTGT chụp lại lúc đặt ------------------------------------
            // Chụp lại chứ không đọc từ bảng stores: thuế suất đổi theo nghị quyết,
            // hóa đơn in lại sau một năm phải ra đúng số đã giao cho khách hôm đó.
            e.Property(x => x.TaxMode)
                .HasDefaultValue(0)
                .HasComment("Chế độ thuế đã áp dụng cho đơn. Đơn có trước khi hệ thống tách thuế mang giá trị 0.");
            e.Property(x => x.TaxRatePercent)
                .HasComment("Thuế suất % đã áp dụng cho đơn này.");
            e.Property(x => x.NetAmount)
                .HasComment("Tiền hàng chưa thuế. Luôn thỏa net_amount + tax_amount = grand_total.");
            e.Property(x => x.TaxAmount)
                .HasComment("Tiền thuế GTGT của đơn.");
            e.Property(x => x.StockDeducted)
                .HasComment("Cờ chống trừ kho hai lần khi client retry hoặc người dùng bấm nhanh.");
            e.Property(x => x.StockReturned)
                .HasComment("Cờ chống hoàn kho hai lần khi hủy đơn.");

            // Webhook SePay tìm ngược ra đơn bằng đúng cột này, mỗi lần tiền về
            // là một lần tra. Duy nhất chứ không chỉ là index: hai đơn trùng mã
            // tham chiếu thì tiền của khách này có thể được ghi cho đơn khách kia.
            e.HasIndex(x => x.PaymentRef).IsUnique().HasFilter("payment_ref IS NOT NULL");

            e.Property(x => x.PaymentRef).HasMaxLength(20)
                .HasComment("Nội dung chuyển khoản in trên mã QR, VD MCC7K2M9. Khóa đối soát của webhook SePay.");
            e.Property(x => x.PaidAmount)
                .HasComment("Số tiền thực nhận. Có thể lớn hơn grand_total khi khách chuyển dư.");
            e.Property(x => x.PaymentGatewayId)
                .HasComment("Id giao dịch SePay. Có giá trị = tiền do ngân hàng xác nhận tự động, không phải nhân viên bấm tay.");

            e.Ignore(x => x.GrossProfit);

            e.HasOne(x => x.User).WithMany(u => u.Orders)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items", t => t.HasComment(
                "Dòng món trong đơn. Tên và giá là SNAPSHOT tại thời điểm đặt — " +
                "đổi giá hay đổi tên món sau này không được làm sai đơn cũ."));

            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.ProductId);

            e.Property(x => x.ProductName).HasMaxLength(150).IsRequired();
            e.Property(x => x.VariantName).HasMaxLength(80);
            e.Property(x => x.ModifiersJson).HasColumnType("jsonb")
                .HasComment("Snapshot topping đã chọn: [{modifierId,name,priceDelta}]");
            e.Property(x => x.Note).HasMaxLength(300);

            e.HasOne(x => x.Order).WithMany(o => o.Items)
                .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PaymentTransaction>(e =>
        {
            e.ToTable("payment_transactions", t => t.HasComment(
                "Nhật ký biến động số dư ngân hàng do SePay gửi qua webhook. GIỮ CẢ giao dịch " +
                "không khớp đơn nào — tiền đã vào tài khoản mà hệ thống im lặng bỏ qua là " +
                "trường hợp tệ nhất khi đối soát cuối ngày."));

            // Chỉ mục DUY NHẤT này LÀ toàn bộ cơ chế chống ghi nhận trùng.
            // SePay gửi lại tối đa 7 lần khi server trả lỗi ngoài dải 200-299;
            // nếu lần đầu đã ghi xong rồi mới lỗi ở bước sau, lần gửi lại sẽ
            // đâm vào đây và bị chặn ở tầng cơ sở dữ liệu, không phụ thuộc vào
            // việc mã ứng dụng có nhớ kiểm tra hay không.
            e.HasIndex(x => x.GatewayId).IsUnique();
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.TransactionDate);
            e.HasIndex(x => x.MatchStatus);

            e.Property(x => x.GatewayId)
                .HasComment("Id giao dịch do SePay cấp. Duy nhất — chống ghi nhận một lần chuyển tiền hai lần.");
            e.Property(x => x.Gateway).HasMaxLength(60).IsRequired();
            e.Property(x => x.AccountNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.SubAccount).HasMaxLength(40);
            e.Property(x => x.TransferType).HasMaxLength(10).IsRequired()
                .HasComment("in = tiền vào, out = tiền ra. Chỉ 'in' mới được xét thanh toán đơn.");
            e.Property(x => x.Amount)
                .HasComment("Số tiền giao dịch, đơn vị đồng.");
            e.Property(x => x.Content).HasMaxLength(500).IsRequired()
                .HasComment("Nội dung chuyển khoản nguyên văn từ ngân hàng.");
            e.Property(x => x.ReferenceCode).HasMaxLength(100);
            e.Property(x => x.DetectedRef).HasMaxLength(20)
                .HasComment("Mã tham chiếu của quán dò ra từ nội dung. null = giao dịch không phải trả đơn.");
            e.Property(x => x.Note).HasMaxLength(300)
                .HasComment("Diễn giải tiếng Việt kết quả đối soát — hiện thẳng cho chủ quán đọc.");
            e.Property(x => x.RawPayload).HasColumnType("jsonb")
                .HasComment("Payload webhook nguyên văn. Dữ liệu tiền bạc do bên thứ ba gửi — luôn giữ bản gốc.");

            // SetNull chứ không Cascade: xóa đơn KHÔNG được phép xóa mất bằng
            // chứng tiền đã về tài khoản.
            e.HasOne(x => x.Order).WithMany()
                .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
        });

        // ======================================================================
        //  KHUYẾN MÃI & KẾ HOẠCH AI
        // ======================================================================
        b.Entity<Promotion>(e =>
        {
            e.ToTable("promotions", t => t.HasComment("Chương trình khuyến mãi."));
            e.HasIndex(x => new { x.StoreId, x.Status, x.StartsAt, x.EndsAt })
                .HasDatabaseName("ix_promotions_active_window");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Value)
                .HasComment("PercentOff → số %; AmountOff → số đồng.");
            e.Property(x => x.BannerText).HasMaxLength(120)
                .HasComment("Dòng chữ trên banner trang bán hàng. Do AI viết, tối đa 80 ký tự.");
            e.Property(x => x.SourcePlanId)
                .HasComment("Kế hoạch AI nào sinh ra khuyến mãi này — dùng đo hiệu quả của AI.");
            e.HasOne(x => x.Product).WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<DailyPlan>(e =>
        {
            e.ToTable("daily_plans", t => t.HasComment(
                "BẢN KẾ HOẠCH HẰNG NGÀY do AI Engine sinh lúc chốt sổ. " +
                "Các cột số do BACKEND tính; các cột chữ (headline, summary) do Claude viết. " +
                "AI không bao giờ được tính toán số liệu tiền/lượng."));

            // Mỗi ngày kinh doanh chỉ có đúng một bản kế hoạch
            e.HasIndex(x => new { x.StoreId, x.BusinessDate }).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.GeneratedAt });

            e.Property(x => x.BusinessDate).HasMaxLength(10).IsRequired()
                .HasComment("Ngày kinh doanh yyyy-MM-dd theo giờ VN. Lưu chuỗi để tránh lỗi lệch múi giờ.");
            e.Property(x => x.TotalValueAtRisk)
                .HasComment("Tổng giá trị nguyên liệu có nguy cơ phải bỏ (đồng). Con số nổi bật nhất của UI.");
            e.Property(x => x.RiskAnalysisJson).HasColumnType("jsonb")
                .HasComment("Toàn bộ kết quả phân tích, khớp List<LotRiskDto>. Dùng vẽ biểu đồ.");
            e.Property(x => x.Headline).HasMaxLength(200);
            e.Property(x => x.Summary).HasMaxLength(2000);
            e.Property(x => x.AiModel).HasMaxLength(60);
            e.Property(x => x.AiError).HasMaxLength(1000)
                .HasComment("Lỗi gọi AI. Có lỗi vẫn phải tạo kế hoạch đủ số liệu — lỗi AI không làm mất kế hoạch.");

            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<PlanSuggestion>(e =>
        {
            e.ToTable("plan_suggestions", t => t.HasComment(
                "Đề xuất hành động. Cột chữ do AI viết, cột số do backend tính."));
            e.HasIndex(x => new { x.DailyPlanId, x.Priority });
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Reasoning).HasMaxLength(1500).IsRequired();
            e.Property(x => x.BannerCopy).HasMaxLength(120);
            e.Property(x => x.QuantityAtRisk).HasPrecision(18, 4);
            e.Property(x => x.ExpectedNetBenefit)
                .HasComment("= cứu được + lãi thêm − lãi mất trên số ly vốn dĩ đã bán được giá gốc.");
            e.HasOne(x => x.Plan).WithMany(p => p.Suggestions)
                .HasForeignKey(x => x.DailyPlanId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PlanDecision>(e =>
        {
            e.ToTable("plan_decisions", t => t.HasComment(
                "Quyết định của quản lý. Vừa là nhật ký kiểm toán, vừa là dữ liệu đo chất lượng AI " +
                "(tỷ lệ duyệt là chỉ số tin cậy quan trọng nhất)."));
            e.HasIndex(x => x.DailyPlanId);
            e.Property(x => x.ModifiedPayloadJson).HasColumnType("jsonb");
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne(x => x.Plan).WithMany(p => p.Decisions)
                .HasForeignKey(x => x.DailyPlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Suggestion).WithMany(s => s.Decisions)
                .HasForeignKey(x => x.SuggestionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DailyConsumption>(e =>
        {
            e.ToTable("daily_consumptions", t => t.HasComment(
                "Tiêu thụ nguyên liệu theo ngày, tổng hợp sẵn. " +
                "Tồn tại để mô hình dự báo EWMA không phải quét sổ cái hàng trăm nghìn dòng."));
            e.HasIndex(x => new { x.StoreId, x.IngredientId, x.BusinessDate }).IsUnique();
            e.Property(x => x.BusinessDate).HasMaxLength(10).IsRequired();
            e.Property(x => x.QuantityUsed).HasPrecision(18, 4);
        });

        b.Entity<DailySales>(e =>
        {
            e.ToTable("daily_sales", t => t.HasComment(
                "Doanh số theo món theo ngày. Dùng tính số ly bán TB/ngày khi sinh phương án khuyến mãi."));
            e.HasIndex(x => new { x.StoreId, x.ProductId, x.BusinessDate }).IsUnique();
            e.Property(x => x.BusinessDate).HasMaxLength(10).IsRequired();
        });

        b.Entity<StockShortageLog>(e =>
        {
            e.ToTable("stock_shortage_logs", t => t.HasComment(
                "Nguyên liệu đã chặn việc nhận đơn, gộp theo ngày. Nguồn của danh sách " +
                "'Cần nhập hàng' trên trang Kế hoạch — tồn kho cuối ngày không cho biết " +
                "trong ngày đã phải từ chối bao nhiêu đơn vì thiếu thứ gì."));

            // Duy nhất: mỗi lần bị chặn là CẬP NHẬT dòng của ngày đó, không thêm
            // dòng mới. Hai máy quầy cùng ghi một lúc thì một bên đâm vào chỉ mục
            // và chuyển sang cập nhật (xem RestockService.RecordShortagesAsync).
            e.HasIndex(x => new { x.StoreId, x.BusinessDate, x.IngredientId }).IsUnique();

            e.Property(x => x.BusinessDate).HasMaxLength(10).IsRequired();
            e.Property(x => x.MaxMissingQuantity).HasPrecision(18, 4)
                .HasComment("Lượng thiếu LỚN NHẤT của một lần bị chặn, không phải tổng — bấm lại nhiều lần không được thổi phồng số cần nhập.");
            e.Property(x => x.BlockedCount)
                .HasComment("Số lần bấm nhận đơn bị chặn vì nguyên liệu này trong ngày.");
            e.Property(x => x.AffectedProducts).HasMaxLength(500);

            e.Property(x => x.ResolvedAt)
                .HasComment("Lúc đã nhập bù đủ. NULL = còn treo, danh sách \"Cần nhập hàng\" vẫn báo.");
            e.Property(x => x.RestockedQuantity).HasPrecision(18, 4)
                .HasComment("Tổng lượng đã nhập bù kể từ lần chặn đầu trong ngày.");

            // Truy vấn nóng: "hôm nay còn việc nhập nào chưa xong?" — lọc thẳng
            // trên các dòng chưa nhập bù thay vì quét cả bảng nhật ký.
            e.HasIndex(x => new { x.StoreId, x.BusinessDate })
                .HasFilter("resolved_at IS NULL");

            e.HasOne(x => x.Ingredient).WithMany()
                .HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Cascade);
        });

        // ======================================================================
        //  QUY ƯỚC ĐẶT TÊN: snake_case cho toàn bộ bảng và cột
        //  Lý do: PostgreSQL phân biệt hoa thường và tự chuyển về chữ thường,
        //  nên dùng PascalCase sẽ phải bọc dấu ngoặc kép trong mọi câu SQL viết tay.
        // ======================================================================
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }
        }
    }

    /// <summary>Chuyển PascalCase sang snake_case. VD: "RemainingQuantity" → "remaining_quantity".</summary>
    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            if (char.IsUpper(input[i]))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(input[i]));
            }
            else sb.Append(input[i]);
        }
        return sb.ToString();
    }
}
