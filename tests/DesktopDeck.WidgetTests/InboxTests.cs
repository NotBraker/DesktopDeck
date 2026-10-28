using DesktopDeck.Widgets;

public static class InboxTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 15, 0, 0);

    private static List<InboxLogic.InboxEntry> Three()
    {
        var list = new List<InboxLogic.InboxEntry>();
        InboxLogic.Add(list, "banana idea", Now.AddHours(-3));
        InboxLogic.Add(list, "Apple note", Now.AddHours(-2));
        InboxLogic.Add(list, "cherry thought", Now.AddHours(-1));
        return list;
    }

    public static void AddTrimsAndIgnoresBlank()
    {
        var list = new List<InboxLogic.InboxEntry>();
        Expect.True(InboxLogic.Add(list, "   ", Now) is null, "blank ignored");
        var entry = InboxLogic.Add(list, "  call mum ", Now);
        Expect.Equal("call mum", entry!.Text, "trimmed");
        Expect.Equal("2026-10-04T15:00:00", entry.Created, "stamp");
        Expect.True(entry.Id.Length > 0, "has id");
    }

    public static void SortsWithPinnedFirst()
    {
        var list = Three();
        Expect.Equal("cherry thought", InboxLogic.Visible(list, "newest", true, false)[0].Text, "newest first");
        Expect.Equal("banana idea", InboxLogic.Visible(list, "oldest", true, false)[0].Text, "oldest first");
        Expect.Equal("Apple note", InboxLogic.Visible(list, "abc", true, false)[0].Text, "alphabetical ignores case");
        list[0].Pinned = true;
        Expect.Equal("banana idea", InboxLogic.Visible(list, "newest", true, false)[0].Text, "pinned on top");
    }

    public static void DoneAndArchivedFiltering()
    {
        var list = Three();
        InboxLogic.SetDone(list[2], true, Now);
        list[1].Archived = true;
        Expect.Equal(1, InboxLogic.Visible(list, "newest", false, true).Count, "hide done and archived");
        var shown = InboxLogic.Visible(list, "newest", true, true);
        Expect.Equal(2, shown.Count, "done shown, archived hidden");
        Expect.Equal("cherry thought", shown[^1].Text, "done goes last");
        Expect.Equal(3, InboxLogic.Visible(list, "newest", true, true, showArchived: true).Count, "archive view");
    }

    public static void AutoClearRemovesOldDone()
    {
        var list = Three();
        InboxLogic.SetDone(list[0], true, Now.AddDays(-8));
        InboxLogic.SetDone(list[1], true, Now.AddDays(-2));
        Expect.Equal(0, InboxLogic.AutoClear(list, Now, 0), "0 = never");
        Expect.Equal(1, InboxLogic.AutoClear(list, Now, 7), "one older than a week");
        Expect.Equal(2, list.Count, "two left");
        InboxLogic.SetDone(list[0], false, Now);
        Expect.Equal("", list[0].DoneAt, "undone clears time");
    }

    public static void AgoText()
    {
        Expect.Equal("now", InboxLogic.Ago(InboxLogic.Stamp(Now.AddSeconds(-20)), Now), "seconds");
        Expect.Equal("5m", InboxLogic.Ago(InboxLogic.Stamp(Now.AddMinutes(-5)), Now), "minutes");
        Expect.Equal("3h", InboxLogic.Ago(InboxLogic.Stamp(Now.AddHours(-3)), Now), "hours");
        Expect.Equal("yesterday", InboxLogic.Ago(InboxLogic.Stamp(Now.AddDays(-1)), Now), "yesterday");
        Expect.Equal("", InboxLogic.Ago("garbage", Now), "bad stamp");
    }

    public static void JsonRoundTripAndBullets()
    {
        var list = Three();
        list[0].Pinned = true;
        var back = InboxLogic.Parse(InboxLogic.Serialize(list));
        Expect.Equal(3, back.Count, "count");
        Expect.True(back[0].Pinned, "pinned kept");
        Expect.Equal(0, InboxLogic.Parse("{oops").Count, "bad JSON");
        Expect.Equal(1, InboxLogic.Parse("[{\"text\":\"x\"},{\"text\":\"\"}]").Count, "blank entries dropped");
        Expect.Equal("•", InboxLogic.Bullet("dot"), "dot");
        Expect.Equal("", InboxLogic.Bullet("none"), "none");
    }
}
