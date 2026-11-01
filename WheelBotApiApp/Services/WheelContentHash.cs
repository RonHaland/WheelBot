using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WheelBotApiApp.Services;

public static class WheelContentHash
{
    public static string Compute(IReadOnlyList<string> options)
    {
        var json = JsonSerializer.Serialize(options);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
