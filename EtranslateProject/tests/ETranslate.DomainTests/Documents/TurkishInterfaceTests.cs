using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ETranslate.Web.Localization;
using ETranslate.Web.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ETranslate.DomainTests.Documents;

public sealed class TurkishInterfaceTests
{
    [Fact]
    public void Turkish_ui_keeps_invariant_number_contract_and_ignores_browser_locale()
    {
        var services = new ServiceCollection(); services.AddTurkishInterface();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
        Assert.Equal("tr-TR", options.DefaultRequestCulture.UICulture.Name);
        Assert.Equal(CultureInfo.InvariantCulture, options.DefaultRequestCulture.Culture);
        Assert.Empty(options.RequestCultureProviders);
        Assert.Equal(25.5m, decimal.Parse("25.5", options.DefaultRequestCulture.Culture));
        Assert.Equal("25.5", 25.5m.ToString(options.DefaultRequestCulture.Culture));
    }

    [Fact]
    public void Login_and_template_validation_use_the_shared_turkish_resource()
    {
        var errors = new List<ValidationResult>();
        var login = new LoginInput();
        Assert.False(Validator.TryValidateObject(login, new ValidationContext(login), errors, true));
        Assert.Contains(errors, e => e.ErrorMessage == "E-posta zorunludur.");
        Assert.Contains(errors, e => e.ErrorMessage == "Şifre zorunludur.");
        var template = new TemplateEditForm { Name = "", Footer = new string('x', 2001) };
        errors.Clear(); Assert.False(Validator.TryValidateObject(template, new ValidationContext(template), errors, true));
        Assert.Contains(errors, e => e.ErrorMessage == "Şablon adı zorunludur.");
        Assert.Contains(errors, e => e.ErrorMessage == "Altbilgi en fazla 2000 karakter olabilir.");
    }

    [Fact]
    public void Resource_formatting_never_translates_or_changes_document_arguments()
    {
        const string name = "قالب فارسی <script>literal</script>";
        Assert.Equal($"{name} · Sürüm 2", UiText.Format("TemplateRevisionName", name, 2));
        Assert.Equal("Ayarlar", UiText.Get("Ayarlar"));
        Assert.Throws<KeyNotFoundException>(() => UiText.Get("missing-key"));
        Assert.Equal("1.250,50 TRY", UiText.MoneyTry(1250.50m));
    }

    [Theory]
    [InlineData("25.5", true, "25.5")]
    [InlineData("5e0", true, "5")]
    [InlineData("2,5", false, "0")]
    [InlineData("25,5", false, "0")]
    [InlineData("", false, "0")]
    public async Task Html_margin_binding_never_treats_a_comma_as_a_thousands_separator(string input, bool valid, string expected)
    {
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var values = new FormValueProvider(BindingSource.Form, new FormCollection(new Dictionary<string, StringValues> { ["Form.MarginTop"] = input }), CultureInfo.GetCultureInfo("tr-TR"));
        var context = DefaultModelBindingContext.CreateBindingContext(action, values,
            new EmptyModelMetadataProvider().GetMetadataForType(typeof(decimal)), null, "Form.MarginTop");
        await new InvariantDecimalModelBinder().BindModelAsync(context);
        Assert.Equal(valid, context.Result.IsModelSet);
        if (valid) Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), context.Result.Model);
        else Assert.Equal(UiText.Get("DecimalNumberRequired"), context.ModelState["Form.MarginTop"]!.Errors.Single().ErrorMessage);
    }
}
