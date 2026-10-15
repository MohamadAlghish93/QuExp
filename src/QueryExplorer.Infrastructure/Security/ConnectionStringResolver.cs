using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Infrastructure.Security;

public class ConnectionStringResolver : IConnectionStringResolver
{
    private readonly ISecretProtector _protector;
    private readonly IQueryProviderFactory _providerFactory;

    public ConnectionStringResolver(ISecretProtector protector, IQueryProviderFactory providerFactory)
    {
        _protector = protector;
        _providerFactory = providerFactory;
    }

    public string Resolve(DbConnectionProfile profile)
    {
        // Password is decrypted here and handed straight to the provider's
        // connection-string builder; it's never written back to disk in this form.
        var password = string.IsNullOrEmpty(profile.EncryptedPassword)
            ? string.Empty
            : _protector.Unprotect(profile.EncryptedPassword);

        var provider = _providerFactory.GetProvider(profile.ProviderType);
        return provider.BuildConnectionString(
            profile.Host, profile.Port, profile.Database, profile.Username, password, profile.ExtraOptions);
    }
}
