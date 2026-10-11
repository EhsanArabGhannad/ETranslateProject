using Microsoft.AspNetCore.Http;

namespace ETranslate.SubscriptionAccess;

// New mutations in these route groups inherit enforcement. Reads never call Billing.
public sealed class SubscriptionWriteFilter(SubscriptionWriteClient client) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method))
            return await next(context);
        http.Response.Headers.CacheControl = "private, no-store";
        if (!Guid.TryParse(http.Request.RouteValues["tenantId"]?.ToString(), out var tenantId) || tenantId == Guid.Empty)
            return Results.Problem(statusCode: 500, title: "subscription_scope_missing");
        var status = await client.CheckAsync(tenantId, http.Request.Headers.Authorization.ToString(), http.RequestAborted);
        return status == SubscriptionWriteStatus.Allowed ? await next(context) : Error(status);
    }

    public static IResult Error(SubscriptionWriteStatus status) => status switch
    {
        SubscriptionWriteStatus.Unauthorized => Results.Unauthorized(),
        SubscriptionWriteStatus.Forbidden => Results.StatusCode(403),
        SubscriptionWriteStatus.Required => Problem(402, "subscription_required"),
        SubscriptionWriteStatus.Pending => Problem(503, "subscription_pending"),
        _ => Problem(503, "subscription_unavailable")
    };
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, title: code,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
