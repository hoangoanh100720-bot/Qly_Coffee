using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QlyCoffee.Infrastructure.Persistence;

// ==============================================================================
//  NHÀ MÁY DBCONTEXT DÙNG LÚC THIẾT KẾ (chỉ cho công cụ dòng lệnh EF)
//
//  KHÔNG được dùng ở lúc chạy. Ứng dụng thật lấy DbContext từ DI, với chuỗi kết
//  nối đọc từ .env trong Program.cs.
//
//  VÌ SAO CẦN
//  Không có lớp này thì `dotnet ef migrations add` phải dựng cả dự án Api để tìm
//  DbContext. Điều đó hỏng ở đúng lúc hay xảy ra nhất: backend đang chạy thì
//  file .dll trong thư mục bin bị khóa, build thất bại, và không ai sinh được
//  migration cho tới khi tắt server. Trỏ thẳng vào dự án Infrastructure thì
//  không đụng tới bin của Api.
//
//  CHUỖI KẾT NỐI Ở ĐÂY CHỈ ĐỂ EF BIẾT NÓ ĐANG NÓI CHUYỆN VỚI POSTGRES.
//  Lệnh `migrations add` chỉ so mô hình C# với ảnh chụp mô hình, KHÔNG hề mở kết
//  nối. Chỉ `database update` mới cần chuỗi thật — và lệnh đó nên chạy qua Api
//  (nó tự chạy migration lúc khởi động) hoặc truyền tay bằng --connection.
//
//  CÁCH DÙNG
//      dotnet ef migrations add TenMigration --project backend/QlyCoffee.Infrastructure
// ==============================================================================

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Ưu tiên biến môi trường nếu người chạy có đặt sẵn; không có thì dùng
        // chuỗi giữ chỗ. Cả hai đều không được mở kết nối trong `migrations add`.
        var connectionString =
            Environment.GetEnvironmentVariable("CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=qly_coffee;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
