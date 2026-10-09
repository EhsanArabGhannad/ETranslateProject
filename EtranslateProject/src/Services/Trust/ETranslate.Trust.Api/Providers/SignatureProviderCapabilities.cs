namespace ETranslate.Trust.Api.Providers;

public sealed record SignatureProviderCapabilities(string Provider, bool RealSigningEnabled, bool EImza, bool MobilImza);
public interface ISignatureProviderCapabilities { SignatureProviderCapabilities Current { get; } }
public sealed class UnconfiguredSignatureProvider : ISignatureProviderCapabilities
{
    public SignatureProviderCapabilities Current { get; } = new("NotConfigured", false, false, false);
}
