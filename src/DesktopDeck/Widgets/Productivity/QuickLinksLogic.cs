using System;
using System.Collections.Generic;
using System.Linq;
namespace DesktopDeck.Widgets;
public static class QuickLinksLogic
{
    public readonly record struct Link(string Title, string Target, bool Web);
    public static List<Link> Parse(IEnumerable<string> lines) => lines.Take(100).Select(line => { int split = line.IndexOf('|'); string title = (split < 0 ? line : line[..split]).Trim(); string target = (split < 0 ? line : line[(split + 1)..]).Trim(); bool web = Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"; return new Link(title.Length > 80 ? title[..80] : title, target, web); }).Where(x => x.Title.Length > 0 && x.Target.Length > 0 && x.Target.Length <= 2048).ToList();
}
