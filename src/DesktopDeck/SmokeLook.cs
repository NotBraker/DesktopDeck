using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>
/// Smoke-test switches for the deck-wide look (they only change the isolated preview settings, never the real ones):
/// <c>--look frosted</c> (one look for every widget, frosted glass), <c>--look deck</c> (one look, solid translucent cards),
/// <c>--look frosted-own</c> (the switch off, every widget set to frosted on its own), <c>--smoke-bg stripes</c> (a sharp, colorful picture behind the deck, so blur shows),
/// <c>--plain-cards</c> (the sample widgets without their own card colors), <c>--smoke-bg aurora</c> / <c>aurora-light</c> (a generated abstract picture of soft colored light, for README screenshots),
/// and <c>--customize</c> (renders the Customize window of the Time widget with the deck look on and with its own look).
/// </summary>
internal static class SmokeLook
{
    internal static void Apply(DeckSettings settings, string[] args)
    {
        settings.Appearance ??= new AppearanceSettings();
        int at = Array.IndexOf(args, "--look");
        if (at >= 0 && at + 1 < args.Length)
        {
            var look = settings.Appearance;
            switch (args[at + 1])
            {
                case "frosted": look.ApplyToAll = true; look.CardStyle = "frosted"; break;
                case "deck": look.ApplyToAll = true; look.CardStyle = "glass"; look.CardColor = "#10243A"; look.CardOpacity = 85; look.CornerRadius = 26; look.TextScale = 110; look.Padding = 16; break;
                case "frosted-own": foreach (var item in settings.Items) item.CardStyle = "frosted"; break;
            }
        }
        // --plain-cards: the sample widgets without their own card colors, corners and opacity (every card in the theme's own style).
        if (args.Contains("--plain-cards"))
            foreach (var item in settings.Items)
                foreach (var key in new[] { "_cardColor", "_borderColor", "_radius", "_textColor", "_opacity" }) item.Options.Remove(key);
        int bg = Array.IndexOf(args, "--smoke-bg");
        if (bg >= 0 && bg + 1 < args.Length && args[bg + 1] == "stripes")
            settings.Appearance.Backgrounds[""] = new BackgroundStyle { Kind = "image", ImagePath = StripePicture(), Fit = "fill" };
        // --smoke-bg aurora: a generated abstract picture of soft colored light (for README screenshots; no outside pictures, no wallpaper).
        else if (bg >= 0 && bg + 1 < args.Length && args[bg + 1] == "aurora")
            settings.Appearance.Backgrounds[""] = new BackgroundStyle { Kind = "image", ImagePath = AuroraPicture(dark: true), Fit = "fill" };
        else if (bg >= 0 && bg + 1 < args.Length && args[bg + 1] == "aurora-light")
            settings.Appearance.Backgrounds[""] = new BackgroundStyle { Kind = "image", ImagePath = AuroraPicture(dark: false), Fit = "fill" };
    }

    /// <summary>A generated abstract background: a base color with large soft glows and a few crisp translucent shapes, so frosted glass has something to blur.</summary>
    private static string AuroraPicture(bool dark)
    {
        string path = Path.Combine(Path.GetTempPath(), "DesktopDeck-sample", dark ? "aurora.png" : "aurora-light.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const int width = 1920, height = 1200;
        Color Rgb(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var baseBrush = new LinearGradientBrush(Rgb(dark ? "#0B1026" : "#EAF0FB"), Rgb(dark ? "#1A1038" : "#F7EAF1"), 35);
            dc.DrawRectangle(baseBrush, null, new Rect(0, 0, width, height));
            (double x, double y, double r, string color)[] glows = dark
                ? [(260, 220, 620, "#1FB6A6"), (1500, 160, 560, "#7C3AED"), (900, 760, 680, "#EC4899"), (1700, 980, 520, "#F59E0B"), (160, 1000, 520, "#3B82F6")]
                : [(260, 240, 620, "#8EC5FF"), (1500, 180, 560, "#C4B5FD"), (900, 800, 700, "#FBB6CE"), (1700, 1000, 520, "#FCD9A8"), (150, 1000, 520, "#9AE6D0")];
            foreach (var (x, y, r, color) in glows)
            {
                var c = Rgb(color);
                var brush = new RadialGradientBrush(Color.FromArgb(dark ? (byte)230 : (byte)200, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B)) { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
                dc.DrawEllipse(brush, null, new Point(x, y), r, r);
            }
            // Crisp shapes with soft edges give the glass edges to blur.
            (double x, double y, double r, string color)[] discs = dark
                ? [(173, 560, 113, "#FDE68A"), (1280, 520, 110, "#67E8F9"), (1060, 140, 90, "#F9A8D4"), (1750, 700, 130, "#A5B4FC"), (640, 1010, 100, "#86EFAC")]
                : [(173, 560, 113, "#FFFFFF"), (1280, 520, 110, "#A7F3D0"), (1060, 140, 90, "#FECACA"), (1750, 700, 130, "#BFDBFE"), (640, 1010, 100, "#FDE68A")];
            foreach (var (x, y, r, color) in discs)
            {
                var c = Rgb(color);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(dark ? (byte)200 : (byte)230, c.R, c.G, c.B)), null, new Point(x, y), r, r);
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
        return path;
    }

    /// <summary>A sharp picture of colored stripes and circles in the temp folder.</summary>
    private static string StripePicture()
    {
        string path = Path.Combine(Path.GetTempPath(), "DesktopDeck-sample", "stripes.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x1A, 0x33)), null, new Rect(0, 0, 1920, 1080));
            Color[] colors = [Color.FromRgb(0xFF, 0x5A, 0x5F), Color.FromRgb(0xFF, 0xB3, 0x47), Color.FromRgb(0x4E, 0xD6, 0x9A), Color.FromRgb(0x3E, 0xA8, 0xFF), Color.FromRgb(0xC0, 0x6C, 0xFF)];
            for (int i = 0; i < 24; i++)
                dc.DrawRectangle(new SolidColorBrush(colors[i % colors.Length]), null, new Rect(i * 90 - 200, 0, 46, 1080) );
            for (int i = 0; i < 9; i++)
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xF5, 0xF2, 0xEA)), null, new Point(120 + i * 230, 180 + (i % 3) * 330), 70, 70);
        }
        var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
        return path;
    }

    /// <summary>
    /// Renders the Customize window (<c>--customize</c>: the Time widget; <c>--customize-only a,b,c</c>: those kinds, built-in or plug-in) as it opens, and again with
    /// every group opened (<c>-open</c>). With the deck look on, also with the widget's own look (<c>-own</c>). Window heights go to customize-heights.txt.
    /// </summary>
    internal static bool SnapCustomize(Window owner, DeckSettings settings, string folder, Action<FrameworkElement, string> snap)
    {
        var args = Environment.GetCommandLineArgs();
        int only = Array.IndexOf(args, "--customize-only");
        var kinds = only >= 0 && only + 1 < args.Length ? args[only + 1].Split(',', StringSplitOptions.RemoveEmptyEntries) : [ItemKinds.Clock];
        bool deckLook = settings.Appearance is { ApplyToAll: true };
        var heights = new List<string>();
        foreach (var kind in kinds)
        {
            var plugin = Widgets.WidgetRegistry.Find(kind);
            var item = settings.Items.FirstOrDefault(i => i.Kind == kind) ?? new LauncherItem { Kind = kind, Name = plugin?.Name ?? kind, Accent = plugin?.Accent ?? "", Target = kind == ItemKinds.Launcher ? "notepad.exe" : "" };
            foreach (var (own, suffix) in deckLook ? new[] { (false, "-deck"), (true, "-own") } : new[] { (false, "") })
            {
                bool before = item.OwnLook;
                item.OwnLook = own;
                var editor = new ItemEditor(owner, item, false, settings) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = 0 };
                editor.Show();
                if (editor.Content is ScrollViewer { Content: FrameworkElement body })
                {
                    double margins = body.Margin.Top + body.Margin.Bottom;
                    string line = $"{kind}{suffix}: window {editor.ActualHeight:0} px, content {body.ActualHeight:0} px";
                    snap(Measured(body), Path.Combine(folder, $"customize-{kind}{suffix}.png"));
                    OpenAll(body);
                    snap(Measured(body), Path.Combine(folder, $"customize-{kind}{suffix}-open.png"));
                    heights.Add(line + $", everything open {body.ActualHeight + margins:0} px");
                }
                editor.Close();
                item.OwnLook = before;
            }
        }
        bool ok = CheckCollapsedFields(owner, settings, heights);
        File.WriteAllText(Path.Combine(folder, "customize-heights.txt"), string.Join(Environment.NewLine, heights));
        return ok;
    }

    /// <summary>
    /// Fields inside closed groups: a mistake in one opens its group, and a value typed into closed groups (More options, More look options)
    /// is still read. Uses the Homework widget (its "soon" days sit in More options).
    /// </summary>
    private static bool CheckCollapsedFields(Window owner, DeckSettings settings, List<string> report)
    {
        var plugin = Widgets.WidgetRegistry.Find("homework");
        if (plugin is null) return true;
        var item = new LauncherItem { Kind = "homework", Name = plugin.Name, Accent = plugin.Accent };
        var editor = new ItemEditor(owner, item, false, settings) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = 0 };
        editor.Show();
        bool ok = true;
        void Check(bool condition, string what) { report.Add((condition ? "OK   " : "FAIL ") + what); ok &= condition; }
        TextBox? Box(string label)
        {
            var nodes = new List<object>();
            void Walk(object node)
            {
                nodes.Add(node);
                if (node is DependencyObject d) foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
            }
            Walk(editor);
            int at = nodes.FindIndex(n => n is TextBlock t && t.Text.StartsWith(label, StringComparison.Ordinal));
            return at < 0 ? null : nodes.Skip(at).OfType<TextBox>().FirstOrDefault();
        }
        var sections = new List<Disclosure>();
        void Collect(DependencyObject node)
        {
            if (node is Disclosure d) sections.Add(d);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Collect(child);
        }
        Collect(editor);
        Check(sections.Count >= 3 && sections.All(s => !s.IsOpen), "More options, Look and More look options start closed");
        if (Box("Count as") is { } soon && Box("Text size") is { } size)
        {
            soon.Text = "oops";
            editor.Save();
            var more = sections.FirstOrDefault(s => IsInside(soon, s));
            Check(more is { IsOpen: true }, "a mistake inside More options opens it");
            soon.Text = "5";
            size.Text = "130";
            var read = editor.PreviewCopy();
            Check(read.Options.GetValueOrDefault("warningDays") == "5" && read.Options.GetValueOrDefault("_textScale") == "130", "values in closed groups are still read");
        }
        else Check(false, "found the fields inside the closed groups");
        editor.Close();
        return ok;
    }

    private static bool IsInside(DependencyObject node, DependencyObject ancestor)
    {
        for (var at = node; at is not null; at = LogicalTreeHelper.GetParent(at)) if (ReferenceEquals(at, ancestor)) return true;
        return false;
    }

    private static FrameworkElement Measured(FrameworkElement body)
    {
        // Drawn without the window around it: the window's own margin is dropped so the picture is the whole 540 px width.
        if (body is Panel panel) panel.Background = Theme.Background;
        body.Margin = new Thickness(0);
        body.Measure(new Size(540, double.PositiveInfinity));
        body.Arrange(new Rect(0, 0, 540, body.DesiredSize.Height));
        body.UpdateLayout();
        return body;
    }

    private static void OpenAll(DependencyObject node)
    {
        if (node is Disclosure section) section.IsOpen = true;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) OpenAll(child);
    }
}
