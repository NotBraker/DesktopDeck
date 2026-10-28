using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>
/// The deck's colors and font. The palette is switchable at runtime (<see cref="Apply"/>): every brush below is set once per palette and is then one
/// stable instance (code compares brushes by reference), so read them when you build a view rather than keeping them in static fields.
/// The default is "dusk": warm near-black, parchment text, terracotta accent.
/// </summary>
internal static class Theme
{
    internal static Brush Background { get; private set; } = null!;
    internal static Brush Surface { get; private set; } = null!;
    internal static Brush SurfaceHover { get; private set; } = null!;
    internal static Brush Surface3 { get; private set; } = null!;
    internal static Brush Line { get; private set; } = null!;
    internal static Brush LineStrong { get; private set; } = null!;
    internal static Brush Text { get; private set; } = null!;
    internal static Brush Text2 { get; private set; } = null!;
    internal static Brush Muted { get; private set; } = null!;
    internal static Brush Dim { get; private set; } = null!;
    internal static Brush Primary { get; private set; } = null!;
    internal static Brush Olive { get; private set; } = null!;
    internal static Brush OnPrimary { get; private set; } = null!;
    internal static Brush Danger { get; private set; } = null!;
    /// <summary>A faint wash of the text color for hover and pressed states, and the dimming layer used while editing the layout.</summary>
    internal static Brush Wash { get; private set; } = null!;
    internal static Brush WashStrong { get; private set; } = null!;
    internal static Brush Scrim { get; private set; } = null!;
    /// <summary>The slim grip bar of floating widgets.</summary>
    internal static Brush Grip { get; private set; } = null!;
    /// <summary>The fill of "see-through" cards.</summary>
    internal static Color GlassColor { get; private set; }
    internal static string PrimaryHex { get; private set; } = "";
    internal static string SurfaceHex { get; private set; } = "";
    internal static FontFamily UiFont { get; private set; } = null!;
    /// <summary>The palette in use, and whether it is a dark one.</summary>
    internal static ThemePalette Palette { get; private set; } = ThemePalette.Dusk;
    internal static bool IsDark => Palette.IsDark;
    internal static double ShadowOpacity => Palette.ShadowOpacity;
    private static ResourceDictionary? installed;

    static Theme() => Apply(null, null);

    /// <summary>
    /// Makes <paramref name="settings"/>' theme the current one: rebuilds every brush, the font and (when <paramref name="app"/> is given)
    /// the menu and tooltip styles. Callers rebuild what they show afterwards (<c>DeckHub.ApplyTheme</c> does).
    /// </summary>
    internal static void Apply(ThemeSettings? settings, Application? app)
    {
        var p = Palette = ThemePalette.For(settings);
        Background = Brush(p.Background);
        Surface = Brush(p.Surface);
        SurfaceHover = Brush(p.SurfaceHover);
        Surface3 = Brush(p.Surface3);
        Line = Brush(p.Line);
        LineStrong = Brush(p.LineStrong);
        Text = Brush(p.Text);
        Text2 = Brush(p.Text2);
        Muted = Brush(p.Muted);
        Dim = Brush(p.Dim);
        Primary = Brush(p.Primary);
        Olive = Brush(p.Olive);
        OnPrimary = Brush(p.OnPrimary);
        Danger = Brush(p.Danger);
        Wash = Brush(p.Wash);
        WashStrong = Brush(p.WashStrong);
        Scrim = Brush(p.Scrim);
        Grip = Brush(p.Grip);
        GlassColor = ((SolidColorBrush)Brush(p.Glass)).Color;
        PrimaryHex = p.Primary;
        SurfaceHex = p.Surface;
        UiFont = new FontFamily(ThemeSettings.FamilyOf(ThemeSettings.Normalized(settings?.Copy()).Font));
        if (app is not null) InstallStyles(app);
    }

    internal static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    internal static readonly string[] Swatches = ["#C4684F", "#9B9A5E", "#B47A4E", "#6c63ff", "#00d4ff", "#22c55e", "#f59e0b", "#ef4444", "#ec4899", "#8b5cf6", "#14b8a6"];

    internal static class Glyphs
    {
        internal const string Add = "", Remove = "", Lock = "", Check = "",
            Picture = "", Edit = "", App = "", Script = "", Link = "",
            Settings = "", Play = "", Pause = "", Reset = "", Next = "",
            Left = "", Right = "", Delete = "", Back = "";
        // Widget and menu icons.
        internal const string Monitor = "", Note = "", Countdown = "", Time = "", Timer = "",
            Tasks = "", Goals = "", Counter = "", Music = "",
            Folder = "", Sun = "", Calendar = "", Open = "", OpenFolder = "", Refresh = "",
            Pin = "", Mic = "", Previous = "", NextTrack = "", Search = "", Habits = "";
    }

    /// <summary>
    /// App-wide styles for menus, submenus, separators and tooltips: rounded dark cards with a soft shadow,
    /// an icon column, rounded hover rows, accent check marks and chevrons for submenus.
    /// </summary>
    internal static void InstallStyles(Application app)
    {
        const string card = """
            <Border Margin="6,4,14,16" Padding="5" CornerRadius="12" Background="{BG}" BorderBrush="{LINE}" BorderThickness="1" MinWidth="{MIN}">
              <Border.Effect><DropShadowEffect BlurRadius="20" ShadowDepth="6" Direction="270" Opacity="{SHADOW}" Color="#000000"/></Border.Effect>
              <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
            </Border>
            """;
        string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Style TargetType="ContextMenu">
                <Setter Property="OverridesDefaultStyle" Value="True"/>
                <Setter Property="HasDropShadow" Value="True"/>
                <Setter Property="Foreground" Value="{TEXT}"/>
                <Setter Property="FontFamily" Value="{FONT}"/>
                <Setter Property="FontSize" Value="13"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="ContextMenu">
                      {CARD220}
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
              <Style TargetType="MenuItem">
                <Setter Property="OverridesDefaultStyle" Value="True"/>
                <Setter Property="Foreground" Value="{TEXT}"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="MenuItem">
                      <Grid SnapsToDevicePixels="True">
                        <Border x:Name="Bg" CornerRadius="8" Background="Transparent" Padding="9,7,10,7" MinHeight="34">
                          <Grid>
                            <Grid.ColumnDefinitions>
                              <ColumnDefinition Width="26"/>
                              <ColumnDefinition Width="*"/>
                              <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <ContentPresenter x:Name="Icon" ContentSource="Icon" VerticalAlignment="Center" HorizontalAlignment="Left"/>
                            <TextBlock x:Name="Check" Text="&#xE73E;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" FontSize="13" Foreground="{ACCENT}" VerticalAlignment="Center" Visibility="Collapsed"/>
                            <ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center" Margin="2,0,14,0"/>
                            <TextBlock x:Name="Arrow" Grid.Column="2" Text="&#xE76C;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" FontSize="10" Foreground="{MUTED}" VerticalAlignment="Center" Visibility="Collapsed"/>
                          </Grid>
                        </Border>
                        <Popup x:Name="PART_Popup" Placement="Right" HorizontalOffset="-4" VerticalOffset="-10" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade"
                               IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}">
                          {CARD200}
                        </Popup>
                      </Grid>
                      <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Bg" Property="Background" Value="{HOVER}"/></Trigger>
                        <Trigger Property="IsChecked" Value="True"><Setter TargetName="Check" Property="Visibility" Value="Visible"/><Setter TargetName="Icon" Property="Visibility" Value="Collapsed"/></Trigger>
                        <Trigger Property="HasItems" Value="True"><Setter TargetName="Arrow" Property="Visibility" Value="Visible"/></Trigger>
                        <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
              <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
                <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Separator"><Border Height="1" Margin="10,5" Background="{LINE}"/></ControlTemplate></Setter.Value></Setter>
              </Style>
              <Style TargetType="ScrollBar">
                <Setter Property="OverridesDefaultStyle" Value="True"/>
                <Setter Property="Width" Value="10"/>
                <Setter Property="MinWidth" Value="10"/>
                <Setter Property="Background" Value="Transparent"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="ScrollBar">
                      <Grid Background="Transparent" Width="10">
                        <Track x:Name="PART_Track" IsDirectionReversed="True">
                          <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False" IsTabStop="False"/></Track.DecreaseRepeatButton>
                          <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False" IsTabStop="False"/></Track.IncreaseRepeatButton>
                          <Track.Thumb>
                            <Thumb>
                              <Thumb.Template>
                                <ControlTemplate TargetType="Thumb"><Border Margin="2,0" CornerRadius="3" Background="{THUMB}"/></ControlTemplate>
                              </Thumb.Template>
                            </Thumb>
                          </Track.Thumb>
                        </Track>
                      </Grid>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
                <Style.Triggers>
                  <Trigger Property="Orientation" Value="Horizontal"><Setter Property="Width" Value="Auto"/><Setter Property="Height" Value="0"/></Trigger>
                </Style.Triggers>
              </Style>
              <Style TargetType="ToolTip">
                <Setter Property="OverridesDefaultStyle" Value="True"/>
                <Setter Property="HasDropShadow" Value="True"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="ToolTip">
                      <Border Margin="4,2,10,12" Padding="10,7" CornerRadius="8" Background="{SURFACE3}" BorderBrush="{LINE}" BorderThickness="1" MaxWidth="380">
                        <Border.Effect><DropShadowEffect BlurRadius="14" ShadowDepth="4" Direction="270" Opacity="{TIPSHADOW}" Color="#000000"/></Border.Effect>
                        <ContentPresenter TextElement.Foreground="{TEXT}" TextElement.FontSize="12" TextElement.FontFamily="{FONT}">
                          <ContentPresenter.Resources><Style TargetType="TextBlock"><Setter Property="TextWrapping" Value="Wrap"/></Style></ContentPresenter.Resources>
                        </ContentPresenter>
                      </Border>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
            </ResourceDictionary>
            """;
        var p = Palette;
        xaml = xaml.Replace("{CARD220}", card.Replace("{MIN}", "220")).Replace("{CARD200}", card.Replace("{MIN}", "200"))
            .Replace("{BG}", p.Surface).Replace("{LINE}", p.LineStrong).Replace("{TEXT}", p.Text).Replace("{MUTED}", p.Muted)
            .Replace("{ACCENT}", p.Primary).Replace("{HOVER}", p.Surface3).Replace("{SURFACE3}", p.Surface3)
            .Replace("{THUMB}", HexColor.WithAlpha(p.Text, 0.27)).Replace("{FONT}", UiFont.Source)
            .Replace("{SHADOW}", p.ShadowOpacity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{TIPSHADOW}", (p.ShadowOpacity * 0.9).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        if (installed is not null) app.Resources.MergedDictionaries.Remove(installed);
        installed = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
        app.Resources.MergedDictionaries.Add(installed);
    }

    internal static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// An accent that was never changed (the original terracotta or olive) follows the theme's own accent, so a new theme or accent
    /// reaches every widget that still has its default; a color the user chose is kept as it is.
    /// </summary>
    private static string Resolve(string color)
    {
        string text = color.Trim();
        if (string.Equals(text, ThemePalette.OriginalPrimary, StringComparison.OrdinalIgnoreCase)) return PrimaryHex;
        if (string.Equals(text, ThemePalette.OriginalOlive, StringComparison.OrdinalIgnoreCase)) return Palette.Olive;
        return text;
    }

    internal static Brush Accent(string? color)
    {
        try { return string.IsNullOrWhiteSpace(color) ? Primary : Brush(Resolve(color)); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { return Primary; }
    }

    /// <summary>An opaque blend of two colors: <paramref name="amount"/> of <paramref name="over"/> on <paramref name="under"/>.</summary>
    internal static Brush Mix(string under, string? over, double amount)
    {
        var a = ((SolidColorBrush)Accent(under)).Color;
        var b = ((SolidColorBrush)Accent(over)).Color;
        byte Blend(byte x, byte y) => (byte)Math.Round(x + (y - x) * amount);
        var brush = new SolidColorBrush(Color.FromRgb(Blend(a.R, b.R), Blend(a.G, b.G), Blend(a.B, b.B)));
        brush.Freeze();
        return brush;
    }

    /// <summary>The accent at reduced opacity, for fills behind accent-colored content.</summary>
    internal static Brush Tint(string? color, double opacity)
    {
        var brush = ((SolidColorBrush)Accent(color)).Clone();
        brush.Opacity = opacity;
        brush.Freeze();
        return brush;
    }

    internal static TextBlock Label(string text, double size = 13, Brush? color = null)
        => new() { Text = text, FontFamily = UiFont, FontSize = size, Foreground = color ?? Text, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Small, spaced, uppercase widget heading (WPF has no letter-spacing, so hair spaces stand in).</summary>
    internal static TextBlock WidgetTitle(string text, Brush? color = null)
    {
        var label = Label(string.Join(" ", text.ToUpperInvariant().ToCharArray()), 10.5, color ?? Muted);
        label.FontWeight = FontWeights.SemiBold;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        return label;
    }

    internal static TextBlock Icon(string glyph, double size, Brush? color = null)
        => new() { Text = glyph, FontFamily = IconFont, FontSize = size, Foreground = color ?? Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A small square button showing one icon glyph.</summary>
    internal static Button IconButton(string glyph, string tooltip, double size = 28, bool accent = false)
    {
        var button = Button("", accent);
        button.Content = Icon(glyph, size * 0.42, accent ? OnPrimary : Text);
        button.ToolTip = tooltip;
        button.Width = button.Height = button.MinHeight = size;
        button.Padding = new Thickness(0);
        if (!accent) { button.Background = Surface3; button.BorderBrush = Brushes.Transparent; }
        button.Focusable = false;
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        return button;
    }

    internal static Button Button(string text, bool prominent = false)
    {
        var button = new Button
        {
            Content = text,
            FontFamily = UiFont,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = prominent ? OnPrimary : Text,
            Background = prominent ? Primary : Surface,
            BorderBrush = prominent ? Primary : LineStrong,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 8, 14, 8),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            MinHeight = 34,
        };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.7));
        template.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
        template.Triggers.Add(disabled);
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Control.BorderBrushProperty, Text));
        template.Triggers.Add(focused);
        button.Template = template;
        return button;
    }

    /// <summary>Pill-shaped segmented tabs; the selected one is filled with the accent.</summary>
    internal static Grid Segmented(string[] labels, int selected, Action<int> select, double height = 26, string? accent = null)
    {
        var grid = new Grid { Height = height };
        var fill = Accent(accent ?? PrimaryHex);
        for (int i = 0; i < labels.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            int index = i;
            bool on = i == selected;
            var label = Label(labels[i], 11.5, on ? OnPrimary : Text2);
            label.FontWeight = FontWeights.SemiBold;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var tab = new Border
            {
                CornerRadius = new CornerRadius(height / 2), Background = on ? fill : Brushes.Transparent, Child = label,
                Cursor = Cursors.Hand, Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0),
            };
            tab.MouseLeftButtonUp += (_, e) => { select(index); e.Handled = true; };
            Grid.SetColumn(tab, i);
            grid.Children.Add(tab);
        }
        return grid;
    }

    /// <summary>An on/off switch.</summary>
    internal static Border Switch(bool value, Action<bool> changed)
    {
        var knob = new System.Windows.Shapes.Ellipse { Width = 16, Height = 16, Fill = OnPrimary, VerticalAlignment = VerticalAlignment.Center };
        bool dark = IsDark;
        var track = new Border { Width = 38, Height = 22, CornerRadius = new CornerRadius(11), Cursor = Cursors.Hand, Padding = new Thickness(3), Child = knob, Focusable = false };
        void Show() { track.Background = value ? Primary : Surface3; knob.HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left; knob.Fill = value || dark ? OnPrimary : Muted; knob.Opacity = value || !dark ? 1 : 0.6; }
        track.MouseLeftButtonUp += (_, e) => { value = !value; Show(); changed(value); e.Handled = true; };
        Show();
        return track;
    }

    internal static TextBox TextField(string? value)
        => new()
        {
            Text = value ?? "", Background = Surface3, Foreground = Text,
            BorderBrush = LineStrong, BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7), FontSize = 13, FontFamily = UiFont,
            CaretBrush = Primary, SelectionBrush = Primary,
        };

    internal static PasswordBox SecretField()
        => new()
        {
            Background = Surface3, Foreground = Text, BorderBrush = LineStrong, BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7), FontSize = 13, CaretBrush = Primary, SelectionBrush = Primary,
        };

    internal static string[] DayLetters => Models.Planner.DayNames.Select(d => d[..1]).ToArray();
}
