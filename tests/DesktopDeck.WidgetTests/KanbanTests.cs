using DesktopDeck.Widgets;
public static class KanbanTests
{
    public static void MovingInAndOutOfDoneUpdatesRetention() { var card = new KanbanLogic.Card { Column = 0 }; var now = new DateTime(2026, 10, 4); KanbanLogic.Move(card, 2, 3, now); Check.Equal(now, card.Completed!.Value); Check.True(!KanbanLogic.Visible(card, 2, 2, now.AddDays(3))); KanbanLogic.Move(card, 1, 3, now); Check.True(card.Completed is null); Check.True(KanbanLogic.Visible(card, 2, 2, now.AddDays(3))); }
    public static void MalformedAndOutOfRangeCardsAreSafe() { Check.Equal(0, KanbanLogic.Parse("broken", 3).Count); var cards = KanbanLogic.Parse("[{\"text\":\"Task\",\"column\":99}]", 3); Check.Equal(2, cards[0].Column); Check.True(cards[0].Id.Length > 0); Check.Equal(3, KanbanLogic.Columns([]).Length); }
}

file static class Check { public static void True(bool value) => Expect.True(value, "condition"); public static void Equal<T>(T expected, T actual) => Expect.Equal(expected, actual, "value"); }
