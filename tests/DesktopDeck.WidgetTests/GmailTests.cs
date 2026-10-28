using DesktopDeck.Models;
using DesktopDeck.Widgets;

/// <summary>Gmail widget logic (GmailLogic.cs): cache, links, grouping, initials and label colors.</summary>
public static class GmailTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 10, 0, 0);

    private static GmailMessage M(string id, string name, string email, string subject, DateTime? local, bool unread = true, params string[] labels) => new()
    {
        Id = id, ThreadId = "t" + id, SenderName = name, SenderEmail = email, Subject = subject, Unread = unread,
        Date = local is { } d ? new DateTimeOffset(d) : null, LabelIds = labels.Length > 0 ? labels.ToList() : ["INBOX"],
    };

    public static void CacheRoundTripsAndKeepsOnlyUsedLabelNames()
    {
        var messages = new[] { M("1", "Ada", "ada@x.org", "Hi", Now, true, "INBOX", "Label_1"), M("2", "Bo", "bo@x.org", "Yo", Now.AddHours(-3), false) };
        string json = GmailLogic.ToCache(messages, 12, [new GmailLabel("Label_1", "Work"), new GmailLabel("Label_9", "Unused")]);
        var back = GmailLogic.FromCache(json);
        Expect.Equal(2, back.Messages.Count, "messages");
        Expect.Equal(12, back.Unread ?? -1, "unread count");
        Expect.True(back.Messages[0].LabelIds.SequenceEqual(["INBOX", "Label_1"]) && back.Messages[0].SenderName == "Ada" && back.Messages[1].Unread == false, "message fields");
        Expect.Equal(1, back.LabelNames.Count, "only labels in use are cached");
        Expect.Equal("Work", back.LabelNames["Label_1"], "label name");
        Expect.True(!json.Contains("body", StringComparison.OrdinalIgnoreCase), "no message bodies in the cache");
    }

    public static void BadCacheGivesEmptyData()
    {
        Expect.Equal(0, GmailLogic.FromCache("{bad").Messages.Count, "bad json");
        Expect.Equal(0, GmailLogic.FromCache("").Messages.Count, "empty text");
        Expect.Equal(0, GmailLogic.FromCache("null").Messages.Count, "null");
        Expect.Equal(1, GmailLogic.FromCache("""{"messages":[{"id":"a"},{"id":""},null]}""").Messages.Count, "entries without an id are dropped");
    }

    public static void QueryDefaultsWhenEmpty()
    {
        Expect.Equal("in:inbox is:unread", GmailLogic.QueryOf("  "), "blank");
        Expect.Equal("label:work", GmailLogic.QueryOf(" label:work "), "trimmed");
    }

    public static void LinksOpenTheConversation()
    {
        Expect.Equal("https://mail.google.com/mail/u/0/#inbox/t1", GmailLogic.LinkFor(M("1", "A", "a@b.c", "s", Now)), "inbox mail opens #inbox/");
        Expect.Equal("https://mail.google.com/mail/u/0/#all/t2", GmailLogic.LinkFor(M("2", "A", "a@b.c", "s", Now, true, "Label_1")), "other mail opens #all/");
        Expect.Equal("https://mail.google.com/mail/u/0/#all/t3", GmailLogic.LinkFor(M("3", "A", "a@b.c", "s", Now, true, "SENT")), "sent mail opens #all/");
        var noThread = new GmailMessage { Id = "x/y", LabelIds = ["INBOX"] };
        Expect.Equal("https://mail.google.com/mail/u/0/#inbox/x%2Fy", GmailLogic.LinkFor(noThread), "falls back to the message id, escaped");
    }

    public static void InitialsFromNameOrAddress()
    {
        Expect.Equal("PN", GmailLogic.InitialsOf(M("1", "Priya Nair", "p@x.org", "s", Now)), "two words");
        Expect.Equal("C", GmailLogic.InitialsOf(M("1", "Campus", "p@x.org", "s", Now)), "one word");
        Expect.Equal("JS", GmailLogic.InitialsOf(M("1", "john.smith@x.org", "john.smith@x.org", "s", Now)), "name that is an address uses the part before @");
        Expect.Equal("É", GmailLogic.InitialsOf(M("1", "élan", "e@x.org", "s", Now)), "accented capital");
        Expect.Equal("(no subject)", GmailLogic.SubjectOf(M("1", "A", "a@b.c", "", Now)), "empty subject");
    }

    public static void BadgeAndAgeWording()
    {
        Expect.Equal("7", GmailLogic.BadgeText(7), "small");
        Expect.Equal("100+", GmailLogic.BadgeText(250), "capped");
        Expect.Equal("14m", GmailLogic.AgeOf(M("1", "A", "a@b.c", "s", Now.AddMinutes(-14)), new DateTimeOffset(Now)), "minutes");
        Expect.Equal("3h", GmailLogic.AgeOf(M("1", "A", "a@b.c", "s", Now.AddHours(-3)), new DateTimeOffset(Now)), "hours");
        Expect.Equal("", GmailLogic.AgeOf(M("1", "A", "a@b.c", "s", null), new DateTimeOffset(Now)), "no date");
    }

    public static void GroupsByDay()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var list = new[]
        {
            M("1", "A", "a@b.c", "one", Now.AddHours(-1)), M("2", "B", "b@b.c", "two", Now.AddHours(-2)),
            M("3", "C", "c@b.c", "three", Now.AddDays(-1)), M("4", "D", "d@b.c", "four", Now.AddDays(-3)), M("5", "E", "e@b.c", "five", Now.AddDays(-20)), M("6", "F", "f@b.c", "six", null),
        };
        var sections = GmailLogic.Layout(list, 10, true, Now, culture);
        Expect.True(sections.Select(s => s.Title).SequenceEqual(["Today", "Yesterday", "Friday", "Sep 15", "Earlier"]), "day headings: " + string.Join(", ", sections.Select(s => s.Title)));
        Expect.Equal(2, sections[0].Items.Count, "two messages today");
        var flat = GmailLogic.Layout(list, 3, false, Now, culture);
        Expect.True(flat.Count == 1 && flat[0].Title == "" && flat[0].Items.Count == 3, "flat list is cut to the limit");
        Expect.Equal(0, GmailLogic.Layout([], 5, false, Now).Count, "nothing to show");
        Expect.Equal("Oct 3, 2025", GmailLogic.DayLabel(new DateTime(2025, 10, 3), Now, culture), "an older year shows the year");
    }

    public static void LabelColorsPickTheFirstMatchingRule()
    {
        var rules = GmailLogic.ParseLabelColors(["Work | #4285f4", "promotions|#f59e0b", "bad line", "NoColor | red", "Starred | #e44", " | #fff"]);
        Expect.Equal(3, rules.Count, "valid rules only");
        var names = new Dictionary<string, string> { ["Label_1"] = "Work" };
        Expect.Equal("#4285f4", GmailLogic.AccentFor(M("1", "A", "a@b.c", "s", Now, true, "INBOX", "Label_1"), rules, names) ?? "", "matches by label name");
        Expect.Equal("#f59e0b", GmailLogic.AccentFor(M("2", "A", "a@b.c", "s", Now, true, "INBOX", "CATEGORY_PROMOTIONS"), rules, names) ?? "", "matches a system category without CATEGORY_");
        Expect.Equal("#e44", GmailLogic.AccentFor(M("3", "A", "a@b.c", "s", Now, true, "STARRED"), rules, names) ?? "", "matches a system label id, any letter case");
        Expect.Equal("#4285f4", GmailLogic.AccentFor(M("4", "A", "a@b.c", "s", Now, true, "STARRED", "Label_1"), rules, names) ?? "", "the first rule wins");
        Expect.True(GmailLogic.AccentFor(M("5", "A", "a@b.c", "s", Now), rules, names) is null, "no matching label, no color");
        Expect.True(GmailLogic.AccentFor(M("6", "A", "a@b.c", "s", Now), [], names) is null, "no rules, no color");
    }

    public static void AvatarColorIsStablePerSender()
    {
        int a = GmailLogic.ColorBucket(M("1", "Ada", "ADA@x.org", "s", Now), 8), b = GmailLogic.ColorBucket(M("2", "Someone else", "ada@x.org", "s", Now), 8);
        Expect.Equal(a, b, "same address, same color");
        Expect.True(a is >= 0 and < 8, "in range");
    }
}
