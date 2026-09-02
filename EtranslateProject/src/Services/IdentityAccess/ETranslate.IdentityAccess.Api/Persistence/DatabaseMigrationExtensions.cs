using Microsoft.EntityFrameworkCore;

namespace ETranslate.IdentityAccess.Api.Persistence;

public static class DatabaseMigrationExtensions
{
    public static async Task ApplyDatabaseMigrationsAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityAccessDbContext>();
        await database.Database.MigrateAsync();
    }
}
