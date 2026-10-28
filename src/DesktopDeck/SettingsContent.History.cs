using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DesktopDeck.Models;

namespace DesktopDeck;

// Settings → History: how many days each kind of history is kept. Each row has a dropdown: the built-in default first, then presets.
internal sealed partial class SettingsContent
{
    /// <summary>The histories listed here, with their plain names. Game time is left out of the public edition along with its widget.</summary>
    private static readonly (string Key, string Name)[] HistoryRows =
    [
        (HistoryKeys.Timer, "Focus timer sessions"),
        (HistoryKeys.Habits, "Habit check-ins"),
        (HistoryKeys.Focus, "Today's focus"),
        (HistoryKeys.Journal, "Journal entries"),
        (HistoryKeys.Hydration, "Water log"),
        (HistoryKeys.Bedtime, "Sleep log"),
        (HistoryKeys.StudyLog, "Study sessions"),
        (HistoryKeys.Homework, "Finished homework"),
        (HistoryKeys.Snapshots, "Settings backups"),
        (HistoryKeys.Trash, "Recycle bin"),
    ];

    /// <summary>The presets in days, shortest first (0 = forever).</summary>
    private static readonly int[] HistoryPresets = [7, 14, 30, 91, 182, 365, 1095, 0];

    private void BuildHistory(StackPanel root)
    {
        var section = Collapsed(root, "settings.history", "Keep history for…", "How long old entries are kept");
        section.Children.Add(Hint("How long Desktop Deck remembers things. The defaults are fine for most people. Older entries go the next time that widget saves."));
        foreach (var (key, name) in HistoryRows) section.Children.Add(HistoryRow(key, name));
    }

    private Grid HistoryRow(string key, string name)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, PanelKit.S16) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Theme.Label(name, 13.5);
        label.FontWeight = FontWeights.SemiBold;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, PanelKit.S16, 0);
        row.Children.Add(label);

        var pick = Theme.Button(HistoryLabel(key, HistorySaved(key)) + "  ▾");
        pick.MinWidth = 150;
        pick.HorizontalAlignment = HorizontalAlignment.Right;
        pick.ToolTip = "How long to keep this";
        pick.Click += (_, _) =>
        {
            int? current = HistorySaved(key);
            var menu = new ContextMenu { PlacementTarget = pick, Placement = PlacementMode.Bottom };
            foreach (int? choice in HistoryChoices(current))
            {
                var item = new MenuItem { Header = HistoryLabel(key, choice), IsCheckable = true, IsChecked = choice == current };
                item.Click += (_, _) =>
                {
                    deck.SetHistoryDays(key, choice);
                    pick.Content = HistoryLabel(key, choice) + "  ▾";
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        Grid.SetColumn(pick, 1);
        row.Children.Add(pick);
        return row;
    }

    /// <summary>The days this history keeps when the user has chosen one, else null (the built-in default).</summary>
    private int? HistorySaved(string key) => settings.History.Days.TryGetValue(key, out int days) ? (int?)days : null;

    /// <summary>The menu: the built-in default (null), the presets, and the saved value when it is not a preset (edited by hand).</summary>
    private static List<int?> HistoryChoices(int? current)
    {
        var choices = new List<int?> { null };
        choices.AddRange(HistoryPresets.Select(d => (int?)d));
        if (current is { } days && !HistoryPresets.Contains(days)) choices.Add(days);
        return choices;
    }

    /// <summary>The text for one choice: "Default (7 days)" for null, else a plain length such as "1 month" or "Forever".</summary>
    private static string HistoryLabel(string key, int? days)
    {
        if (days is not { } value)
        {
            int fallback = HistoryKeys.Defaults.GetValueOrDefault(key);
            return fallback == 0 ? "Default (forever)" : $"Default ({fallback} days)";
        }
        return value switch
        {
            0 => "Forever",
            1 => "1 day",
            7 => "1 week",
            14 => "2 weeks",
            30 => "1 month",
            91 => "3 months",
            182 => "6 months",
            365 => "1 year",
            1095 => "3 years",
            _ => value + " days",
        };
    }
}
