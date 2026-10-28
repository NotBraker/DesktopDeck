using DesktopDeck.Widgets;
public static class QuickLinksTests
{
    public static void LinksKeepPathsAndRecognizeOnlyWebSchemes() { var links = QuickLinksLogic.Parse(["Docs | https://example.com/a?x=1", "Files | C:\\Projects", " | "]); Check.Equal(2, links.Count); Check.True(links[0].Web); Check.True(!links[1].Web); Check.Equal("C:\\Projects", links[1].Target); }
    public static void InputsAreBounded() { Check.Equal(100, QuickLinksLogic.Parse(Enumerable.Repeat("Site | https://example.com", 200)).Count); Check.Equal(0, QuickLinksLogic.Parse(["Huge | " + new string('x', 2049)]).Count); }
}

file static class Check { public static void True(bool value) => Expect.True(value, "condition"); public static void Equal<T>(T expected, T actual) => Expect.Equal(expected, actual, "value"); }
