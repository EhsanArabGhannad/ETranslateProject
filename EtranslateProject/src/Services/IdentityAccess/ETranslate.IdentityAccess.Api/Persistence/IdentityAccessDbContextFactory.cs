using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ETranslate.IdentityAccess.Api.Persistence;

public sealed class IdentityAccessDbContextFactory : IDesignTimeDbContextFactory<IdentityAccessDbContext>
{
    public IdentityAccessDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityAccessDbContext>()
            .UseSqlServer(
                "Server=.\\ESIMSSQLSERVER;Database=ETranslateIdentity;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
            .Options;

        return new IdentityAccessDbContext(options);
    }
}
