using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ETranslate.Billing.Api.Persistence;

public sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseSqlServer(
                "Server=.\\ESIMSSQLSERVER;Database=ETranslateBilling;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
            .Options;

        return new BillingDbContext(options);
    }
}
