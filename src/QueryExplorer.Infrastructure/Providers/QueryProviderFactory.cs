using QueryExplorer.Application.Interfaces;
using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Infrastructure.Providers;

public class QueryProviderFactory : IQueryProviderFactory
{
    private readonly IEnumerable<Application.Interfaces.IQueryProvider> _providers;

    public QueryProviderFactory(IEnumerable<Application.Interfaces.IQueryProvider> providers)
    {
        _providers = providers;
    }

    public Application.Interfaces.IQueryProvider GetProvider(DatabaseProviderType providerType)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderType == providerType);
        if (provider is null)
            throw new NotSupportedException($"No IQueryProvider registered for '{providerType}'.");
        return provider;
    }
}
