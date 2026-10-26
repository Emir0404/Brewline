using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Brewline.Web.Services;

public static partial class Tokens
{
    /// <summary>128 random bits as url-safe text: for cart cookies and order links. Cannot be guessed or counted up.</summary>
    public static string New() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>"Ethiopia Yirgacheffe (250 g)" becomes "ethiopia-yirgacheffe-250-g".</summary>
    public static string Slugify(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder();
        foreach (var c in decomposed)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                plain.Append(char.ToLowerInvariant(c));

        var slug = NonSlugChars().Replace(plain.ToString(), "-").Trim('-');
        return slug.Length == 0 ? "product" : slug.Length > 70 ? slug[..70].TrimEnd('-') : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();
}
