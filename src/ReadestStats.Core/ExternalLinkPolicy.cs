namespace ReadestStats.Core;

public static class ExternalLinkPolicy
{
    public static bool TryGetWebUri(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(candidate.Host) || !string.IsNullOrEmpty(candidate.UserInfo)) return false;
        uri = candidate;
        return true;
    }
}
