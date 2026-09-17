using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ETranslate.TranslationWorkflow.Api.Persistence;

public sealed class TranslationWorkflowDbContextFactory :
    IDesignTimeDbContextFactory<TranslationWorkflowDbContext>
{
    public TranslationWorkflowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TranslationWorkflowDbContext>()
            .UseSqlServer(
                "Server=.\\ESIMSSQLSERVER;Database=ETranslateWorkflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
            .Options;

        return new TranslationWorkflowDbContext(options);
    }
}
