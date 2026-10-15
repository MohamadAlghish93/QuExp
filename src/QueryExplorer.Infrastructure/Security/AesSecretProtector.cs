using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using QueryExplorer.Application.Interfaces;

namespace QueryExplorer.Infrastructure.Security;

/// <summary>
/// AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC) protection for secrets at rest.
/// Both keys are derived from the Encryption:Key configuration value, so
/// connections.json on its own is useless without that key.
///
/// IMPORTANT: this protects the FILE, not the running application. Anyone who can
/// read the app's configuration (or its memory) can decrypt. That is unavoidable -
/// the app must be able to decrypt unattended to open connections. Keep
/// Encryption:Key out of source control and supply it via an environment variable
/// or secrets manager in production. Changing the key makes existing stored
/// passwords unreadable; they must be re-entered.
/// </summary>
public class AesSecretProtector : ISecretProtector
{
    private readonly byte[] _encryptionKey;
    private readonly byte[] _macKey;

    public AesSecretProtector(IConfiguration config)
    {
        var configuredKey = config["Encryption:Key"];
        if (string.IsNullOrWhiteSpace(configuredKey) || configuredKey.Length < 32)
            throw new InvalidOperationException(
                "Encryption:Key must be configured and at least 32 characters long.");

        // Two independent keys from one configured secret, so the same bytes
        // are never used for both encryption and authentication.
        using var sha = SHA256.Create();
        _encryptionKey = sha.ComputeHash(Encoding.UTF8.GetBytes("enc:" + configuredKey));
        _macKey = sha.ComputeHash(Encoding.UTF8.GetBytes("mac:" + configuredKey));
    }

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;

        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Layout: [16-byte IV][ciphertext][32-byte HMAC over IV+ciphertext]
        var ivAndCipher = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, ivAndCipher, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, ivAndCipher, aes.IV.Length, cipherBytes.Length);

        using var hmac = new HMACSHA256(_macKey);
        var tag = hmac.ComputeHash(ivAndCipher);

        var output = new byte[ivAndCipher.Length + tag.Length];
        Buffer.BlockCopy(ivAndCipher, 0, output, 0, ivAndCipher.Length);
        Buffer.BlockCopy(tag, 0, output, ivAndCipher.Length, tag.Length);

        return Convert.ToBase64String(output);
    }

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return string.Empty;

        var raw = Convert.FromBase64String(protectedValue);
        const int ivLength = 16;
        const int tagLength = 32;
        if (raw.Length < ivLength + tagLength)
            throw new CryptographicException("Stored secret is malformed.");

        var ivAndCipher = new byte[raw.Length - tagLength];
        var tag = new byte[tagLength];
        Buffer.BlockCopy(raw, 0, ivAndCipher, 0, ivAndCipher.Length);
        Buffer.BlockCopy(raw, ivAndCipher.Length, tag, 0, tagLength);

        // Verify before decrypting, so tampered data is rejected outright.
        using var hmac = new HMACSHA256(_macKey);
        var expectedTag = hmac.ComputeHash(ivAndCipher);
        if (!CryptographicOperations.FixedTimeEquals(expectedTag, tag))
            throw new CryptographicException(
                "Stored secret failed integrity check - it was tampered with, or Encryption:Key has changed.");

        var iv = new byte[ivLength];
        var cipherBytes = new byte[ivAndCipher.Length - ivLength];
        Buffer.BlockCopy(ivAndCipher, 0, iv, 0, ivLength);
        Buffer.BlockCopy(ivAndCipher, ivLength, cipherBytes, 0, cipherBytes.Length);

        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
