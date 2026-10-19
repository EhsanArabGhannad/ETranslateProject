using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace ETranslate.Web.Localization;

// One interface language. Document language/direction is user data, never a request locale.
public static class TurkishInterface
{
    public static IServiceCollection AddTurkishInterface(this IServiceCollection services)
    {
        services.Configure<RequestLocalizationOptions>(options =>
        {
            // HTML number inputs and existing form/API contracts use decimal points.
            // Turkish UI must not reinterpret 25.5 as 255 or reject saved margin values.
            options.DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("tr-TR"));
            options.SupportedCultures = [CultureInfo.InvariantCulture];
            options.SupportedUICultures = [CultureInfo.GetCultureInfo("tr-TR")];
            options.RequestCultureProviders.Clear();
        });
        return services;
    }

    public static void ConfigureBindingMessages(DefaultModelBindingMessageProvider messages)
    {
        messages.SetAttemptedValueIsInvalidAccessor((_, _) => "Girilen değer bu alan için geçersiz.");
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => "Girilen değer geçersiz.");
        messages.SetUnknownValueIsInvalidAccessor(_ => "Girilen değer bu alan için geçersiz.");
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Girilen değer geçersiz.");
        messages.SetValueIsInvalidAccessor(_ => "Girilen değer geçersiz.");
        messages.SetValueMustNotBeNullAccessor(_ => "Bu alan zorunludur.");
        messages.SetMissingBindRequiredValueAccessor(_ => "Bu alan zorunludur.");
        messages.SetMissingKeyOrValueAccessor(() => "Zorunlu bilgi eksik.");
        messages.SetMissingRequestBodyRequiredValueAccessor(() => "İstek içeriği zorunludur.");
        messages.SetValueMustBeANumberAccessor(_ => "Bu alana bir sayı girin.");
        messages.SetNonPropertyValueMustBeANumberAccessor(() => "Bir sayı girin.");
    }
}
