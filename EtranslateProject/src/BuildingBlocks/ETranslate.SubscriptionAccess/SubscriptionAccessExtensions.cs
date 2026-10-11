using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ETranslate.SubscriptionAccess;

public static class SubscriptionAccessExtensions
{
    public static IServiceCollection AddSubscriptionWriteAccess(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<SubscriptionWriteClient>(http =>
        {
            http.BaseAddress = new Uri("https+http://billing");
            // Bounds the entire GET, including any inherited safe-read retry pipeline.
            http.Timeout = TimeSpan.FromSeconds(4);
        });
        return services;
    }
}
