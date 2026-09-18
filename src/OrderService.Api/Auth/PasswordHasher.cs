using System.Security.Cryptography;

namespace OrderService.Api.Auth;

/// <summary>Hash e verificação de senha via PBKDF2/HMACSHA256. Usa <see cref="Rfc2898DeriveBytes"/> da BCL em vez de trazer uma lib externa tipo BCrypt.Net só pra isso.</summary>
/// <remarks>
/// Formato persistido: <c>{iterations}.{saltBase64}.{hashBase64}</c>. Os
/// parâmetros viajam junto com o hash, então dá pra aumentar
/// <see cref="Iterations"/> no futuro sem invalidar hashes antigos — na
/// verificação sempre lemos o valor gravado, nunca assumimos uma constante fixa.
/// </remarks>
public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encodedHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        if (string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('.', 3);
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var iterations)
            || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expectedHash = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
