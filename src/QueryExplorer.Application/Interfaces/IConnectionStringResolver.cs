using QueryExplorer.Domain.Entities;

namespace QueryExplorer.Application.Interfaces;

/// <summary>
/// Turns a stored profile (with an encrypted password) into a usable connection
/// string. Centralized so decryption happens in exactly one place.
/// </summary>
public interface IConnectionStringResolver
{
    string Resolve(DbConnectionProfile profile);
}
