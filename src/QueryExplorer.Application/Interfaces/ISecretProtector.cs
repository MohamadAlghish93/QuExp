namespace QueryExplorer.Application.Interfaces;

/// <summary>
/// Encrypts/decrypts secrets stored on disk (currently connection passwords).
/// Implementations must be symmetric and keyed from application configuration,
/// so the stored file alone is not enough to recover a password.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
