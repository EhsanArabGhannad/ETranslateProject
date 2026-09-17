using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ETranslate.Documents.Api.Persistence;

public sealed class DocumentsDbContextFactory : IDesignTimeDbContextFactory<DocumentsDbContext>
{
    public DocumentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseSqlServer(
                "Server=.\\ESIMSSQLSERVER;Database=ETranslateDocuments;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
            .Options;

        return new DocumentsDbContext(options);
    }
}
