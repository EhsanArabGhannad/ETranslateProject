using ETranslate.IdentityAccess.Api.Identity;

namespace ETranslate.DomainTests.Tenants;

public sealed class TenantSlugTests
{
    [Theory]
    [InlineData(" My-Office ", "my-office")]
    [InlineData("translator-123", "translator-123")]
    public void Create_NormalizesValidRequestedSlug(string requested, string expected)
    {
        var result = TenantSlug.Create(requested, TenantType.TranslationOffice);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("not valid")]
    [InlineData("ترجمه")]
    [InlineData("-starts-with-hyphen")]
    public void Create_RejectsInvalidRequestedSlug(string requested)
    {
        Assert.Throws<ArgumentException>(() =>
            TenantSlug.Create(requested, TenantType.IndependentTranslator));
    }

    [Theory]
    [InlineData(TenantType.IndependentTranslator, "translator-")]
    [InlineData(TenantType.TranslationOffice, "office-")]
    public void Create_GeneratesSlugWhenNotRequested(TenantType type, string prefix)
    {
        var result = TenantSlug.Create(null, type);

        Assert.StartsWith(prefix, result);
        Assert.InRange(result.Length, 3, 63);
    }
}
