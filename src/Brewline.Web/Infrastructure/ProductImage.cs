using System.Net;
using System.Text;
using Brewline.Web.Domain;

namespace Brewline.Web.Infrastructure;

/// <summary>
/// Product pictures drawn as SVG from the product's data: no stock photos, no licences to worry about,
/// and a new product in the admin immediately has a picture.
/// </summary>
public static class ProductImage
{
    public static string Render(Product product)
    {
        var accent = product.Accent;
        var dark = Shade(accent, 0.62);
        var light = Shade(accent, 1.35);
        var name = WebUtility.HtmlEncode(product.Name);
        var lines = Wrap(product.Name, 14);
        var isCoffee = product.Category == "Coffee";

        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 400 400\" role=\"img\" aria-label=\"").Append(name).Append("\">");
        svg.Append("<rect width=\"400\" height=\"400\" fill=\"#f3ece4\"/>");
        svg.Append("<ellipse cx=\"200\" cy=\"352\" rx=\"112\" ry=\"14\" fill=\"#000\" opacity=\".10\"/>");

        if (isCoffee)
        {
            // A coffee bag: body, folded top, label and a small bean.
            svg.Append($"<path d=\"M110 96 L290 96 L304 340 Q304 352 292 352 L108 352 Q96 352 96 340 Z\" fill=\"{accent}\"/>");
            svg.Append($"<path d=\"M110 96 L290 96 L296 150 L104 150 Z\" fill=\"{dark}\"/>");
            svg.Append($"<rect x=\"104\" y=\"78\" width=\"192\" height=\"26\" rx=\"4\" fill=\"{light}\"/>");
            svg.Append("<rect x=\"122\" y=\"186\" width=\"156\" height=\"128\" rx=\"10\" fill=\"#fffaf3\"/>");
            for (var i = 0; i < lines.Count; i++)
                svg.Append($"<text x=\"200\" y=\"{222 + i * 24}\" text-anchor=\"middle\" font-family=\"Georgia,serif\" font-size=\"19\" font-weight=\"700\" fill=\"{dark}\">{WebWrap(lines[i])}</text>");
            svg.Append($"<ellipse cx=\"200\" cy=\"{232 + lines.Count * 24}\" rx=\"11\" ry=\"15\" fill=\"{accent}\" transform=\"rotate(25 200 {232 + lines.Count * 24})\"/>");
            svg.Append($"<path d=\"M194 {224 + lines.Count * 24} Q202 {232 + lines.Count * 24} 206 {240 + lines.Count * 24}\" stroke=\"#fffaf3\" stroke-width=\"2.5\" fill=\"none\"/>");
        }
        else
        {
            // Equipment: a cone / cup shape with the product's colour.
            svg.Append($"<path d=\"M120 130 L280 130 L246 330 Q244 346 228 346 L172 346 Q156 346 154 330 Z\" fill=\"{accent}\"/>");
            svg.Append($"<ellipse cx=\"200\" cy=\"130\" rx=\"80\" ry=\"16\" fill=\"{light}\"/>");
            svg.Append($"<ellipse cx=\"200\" cy=\"130\" rx=\"66\" ry=\"11\" fill=\"{dark}\"/>");
            svg.Append($"<path d=\"M278 170 Q330 170 326 226 Q322 276 258 280\" stroke=\"{accent}\" stroke-width=\"16\" fill=\"none\" stroke-linecap=\"round\"/>");
            svg.Append("<path d=\"M150 150 L170 320\" stroke=\"#fff\" stroke-width=\"7\" opacity=\".22\" stroke-linecap=\"round\"/>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string WebWrap(string text) => WebUtility.HtmlEncode(text);

    private static List<string> Wrap(string text, int max)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > max)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = current.Length == 0 ? word : $"{current} {word}";
            }
        }
        if (current.Length > 0) lines.Add(current);
        return lines.Take(3).ToList();
    }

    /// <summary>Makes a #rrggbb colour darker (factor &lt; 1) or lighter (factor &gt; 1). Falls back to grey for bad input.</summary>
    internal static string Shade(string hex, double factor)
    {
        if (hex.Length != 7 || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return "#888888";

        static int Clamp(double v) => (int)Math.Clamp(Math.Round(v), 0, 255);
        var r = Clamp(((rgb >> 16) & 0xFF) * factor);
        var g = Clamp(((rgb >> 8) & 0xFF) * factor);
        var b = Clamp((rgb & 0xFF) * factor);
        return $"#{r:x2}{g:x2}{b:x2}";
    }
}
