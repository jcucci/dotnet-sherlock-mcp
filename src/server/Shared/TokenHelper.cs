using System.Text;

namespace Sherlock.MCP.Server.Shared;

public static class TokenHelper
{
    public static string Make(int offset, string salt)
    {
        var payload = $"{offset}:{salt}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    public static bool TryParse(string token, out int offset, out string salt)
    {
        offset = 0;
        salt = string.Empty;
        try
        {
            var data = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = data.Split(':');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], out offset)) return false;
            salt = parts[1];
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string MakeSalt(string seed)
    {
        // Use first 16 chars of SHA256 for compact salt
        var hash = CacheKeyHelper.Sha256(seed);
        return hash.Substring(0, 16);
    }
}

