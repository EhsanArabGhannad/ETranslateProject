using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ETranslate.TranslationWorkflow.Api.Persistence;

public sealed class TranslationWorkflowDbContextFactory :
    IDesignTimeDbContextFactory<TranslationWorkflowDbContext>
{
    public TranslationWorkflowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TranslationWorkflowDbContext>()
            .UseNpgsql("Host=localhost;Database=workflowdb;Username=postgres;Password=postgres")
            .Options;

        return new TranslationWorkflowDbContext(options);
    }
}
