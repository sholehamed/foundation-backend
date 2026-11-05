namespace Modules.System.Bff.Security;

/// <summary>
/// A conservative browser navigation return-path validator for the upcoming BFF
/// OIDC challenge and post-callback redirect. It accepts only a relative path
/// rooted on the current origin; it never resolves URLs or issues redirects.
/// </summary>
public static class BffLocalReturnUrl
{
    public const int MaxLength = 2048;

    /// <summary>
    /// Accepts unencoded, same-origin application paths with an optional query.
    /// The caller must keep the result in protected OIDC AuthenticationProperties
    /// (never in an untrusted state query parameter).
    /// </summary>
    public static bool TryValidate(string? candidate, out string validated)
    {
        validated = string.Empty;

        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Length > MaxLength ||
            !candidate.StartsWith("/", StringComparison.Ordinal) ||
            candidate.StartsWith("//", StringComparison.Ordinal) ||
            !Uri.TryCreate(candidate, UriKind.Relative, out _))
        {
            return false;
        }

        foreach (var c in candidate)
        {
            if (char.IsControl(c) || c is '\\' or '#' or '\u2028' or '\u2029')
                return false;
        }

        var queryIndex = candidate.IndexOf('?');
        var path = queryIndex < 0 ? candidate : candidate[..queryIndex];

        // Reject any percent encoding in the path: it can conceal /, backslash,
        // dot segments or nested encoding that a reverse proxy decodes again.
        // Queries may contain ordinary application-encoded values.
        if (path.Contains('%') || path.Contains("//", StringComparison.Ordinal))
            return false;

        // Dot segments are normalized by some browsers/reverse proxies.
        // Reject before routing or redirecting, including the trailing case.
        if (path.Split('/').Any(segment => segment is "." or ".."))
            return false;

        // Never let encoded line breaks or backslashes through the query.
        // These don't create an external origin by themselves, but avoiding them
        // prevents future downstream header/URL handling inconsistencies.
        if (queryIndex >= 0)
        {
            var query = candidate[(queryIndex + 1)..];
            if (query.Contains("%0d", StringComparison.OrdinalIgnoreCase) ||
                query.Contains("%0a", StringComparison.OrdinalIgnoreCase) ||
                query.Contains("%5c", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        validated = candidate;
        return true;
    }
}
