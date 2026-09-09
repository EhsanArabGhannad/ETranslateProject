using Microsoft.EntityFrameworkCore;

namespace ETranslate.TranslationWorkflow.Api.Persistence;

public static class DatabaseMigrationExtensions
{
    public static async Task ApplyDatabaseMigrationsAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TranslationWorkflowDbContext>();
        await database.Database.MigrateAsync();
    }
}
