using ETranslate.SubscriptionAccess;
using ETranslate.Web.Services;
using System.Net;

namespace ETranslate.Web.Models;

public sealed record SubscriptionWriteNotice(Guid TenantId, SubscriptionWriteStatus Status)
{
    public string Message => new BackendException(HttpStatusCode.ServiceUnavailable, Status switch
    {
        SubscriptionWriteStatus.Required => "subscription_required",
        SubscriptionWriteStatus.Pending => "subscription_pending",
        _ => "subscription_unavailable"
    }).UserMessage;
}
