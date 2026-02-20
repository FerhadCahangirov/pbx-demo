using System.Security.Cryptography;
using System.Text;

namespace CallControl.Api.Services;

public sealed class PasswordHasher
{
    private const string Prefix = "pbkdf2";
    private const int DefaultSaltSize = 16;
    private const int DefaultHashSize = 32;
    private const int DefaultIterations = 100_000;

    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(DefaultSaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            DefaultHashSize);

        return $"{Prefix}${DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string encodedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('$');
        if (parts.Length == 4 && string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return VerifyPbkdf2(password, parts[1], parts[2], parts[3]);
        }

        return string.Equals(encodedHash, password, StringComparison.Ordinal);
    }

    private static bool VerifyPbkdf2(string password, string iterationsRaw, string saltRaw, string hashRaw)
    {
        if (!int.TryParse(iterationsRaw, out var iterations) || iterations < 1)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(saltRaw);
            expectedHash = Convert.FromBase64String(hashRaw);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expectedHash.Length == 0)
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
