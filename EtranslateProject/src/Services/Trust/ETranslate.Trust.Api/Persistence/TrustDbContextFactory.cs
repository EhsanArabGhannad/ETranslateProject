using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ETranslate.Trust.Api.Persistence;

public sealed class TrustDbContextFactory : IDesignTimeDbContextFactory<TrustDbContext>
{
    public TrustDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<TrustDbContext>()
        .UseSqlServer("Server=.\\ESIMSSQLSERVER;Database=ETranslateTrust;Integrated Security=True;Encrypt=True;TrustServerCertificate=True").Options);
}
