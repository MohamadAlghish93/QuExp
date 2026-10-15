using QueryExplorer.Domain.Enums;

namespace QueryExplorer.Application.Interfaces;

public interface IQueryProviderFactory
{
    IQueryProvider GetProvider(DatabaseProviderType providerType);
}
