using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ETranslate.IdentityAccess.Api.Persistence;

public sealed class IdentityAccessDbContextFactory : IDesignTimeDbContextFactory<IdentityAccessDbContext>
{
    public IdentityAccessDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityAccessDbContext>()
            .UseNpgsql("Host=localhost;Database=identitydb;Username=postgres;Password=postgres")
            .Options;

        return new IdentityAccessDbContext(options);
    }
}
