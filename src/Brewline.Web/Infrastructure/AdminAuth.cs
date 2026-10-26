using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Brewline.Web.Infrastructure;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string? Username { get; set; }
    public string? Password { get; set; }
}

/// <summary>
/// The shop has one admin whose credentials come from configuration (user secrets or environment
/// variables, never from the code). With nothing configured the admin area is switched off.
/// </summary>
public sealed class AdminAuth(IOptions<AdminOptions> options)
{
    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(options.Value.Username) && !string.IsNullOrWhiteSpace(options.Value.Password);

    public bool Validate(string? username, string? password)
    {
        if (!IsEnabled) return false;

        // Both comparisons always run and take the same time whatever is wrong, so a timing attack learns nothing.
        var userOk = SafeEquals(username, options.Value.Username!);
        var passOk = SafeEquals(password, options.Value.Password!);
        return userOk & passOk;
    }

    private static bool SafeEquals(string? given, string expected)
    {
        // Hashing first makes both sides the same length, which FixedTimeEquals requires.
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(given ?? ""));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
