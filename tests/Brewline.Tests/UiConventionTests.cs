using System.Text.RegularExpressions;

namespace Brewline.Tests;

/// <summary>
/// Keeps the buttons consistent. Anything that acts like a button (Back, Cancel, Previous...) must look like one,
/// and a link must never pretend to be a button or the other way round.
/// </summary>
public class UiConventionTests
{
    private static readonly Regex Anchor = new(@"<a\b(?<attrs>[^>]*)>(?<text>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    [Fact]
    public void No_view_uses_a_link_styled_button()
    {
        var offenders = Views().Where(v => Regex.IsMatch(v.Text, @"\bbtn-link\b")).Select(v => v.Name).ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Back_cancel_and_paging_links_are_real_buttons()
    {
        var offenders = new List<string>();
        foreach (var (name, text) in Views())
            foreach (Match m in Anchor.Matches(text))
            {
                var label = Regex.Replace(m.Groups["text"].Value, "<[^>]+>", "").Trim();
                if (!Regex.IsMatch(label, @"^(←|&larr;|Back\b|Cancel\b|Odustani\b|Nazad\b|Prethodn|Sljedeć|Previous\b|Next\b)", RegexOptions.IgnoreCase)) continue;
                if (!Regex.IsMatch(m.Groups["attrs"].Value, @"class=""[^""]*\bbtn\b")) offenders.Add($"{name}: {label}");
            }
        Assert.Empty(offenders);
    }

    [Fact]
    public void No_link_goes_nowhere()
    {
        var offenders = Views().Where(v => Regex.IsMatch(v.Text, @"<a\b[^>]*\bhref=""(#|)""", RegexOptions.IgnoreCase)).Select(v => v.Name).ToList();
        Assert.Empty(offenders);
    }

    private static IEnumerable<(string Name, string Text)> Views()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Brewline.Web", "Views"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var views = Path.Combine(dir!.FullName, "src", "Brewline.Web", "Views");
        return Directory.EnumerateFiles(views, "*.cshtml", SearchOption.AllDirectories)
            .Select(f => (Path.GetRelativePath(views, f), File.ReadAllText(f))).ToList();
    }
}
