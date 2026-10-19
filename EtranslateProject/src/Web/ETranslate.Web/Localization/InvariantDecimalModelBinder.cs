using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ETranslate.Web.Localization;

// HTML number controls submit a decimal point regardless of the browser's visible locale.
// Do not accept comma as a thousands separator: "2,5" must never silently become 25 mm.
public sealed class InvariantDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, value);
        if (decimal.TryParse(value.FirstValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            context.Result = ModelBindingResult.Success(number);
        else
        {
            context.ModelState.TryAddModelError(context.ModelName, UiText.Get("DecimalNumberRequired"));
            context.Result = ModelBindingResult.Failed();
        }
        return Task.CompletedTask;
    }
}
