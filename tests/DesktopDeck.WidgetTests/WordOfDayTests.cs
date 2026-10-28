using DesktopDeck.Widgets;

public static class WordOfDayTests
{
    public static void CollectionIsCompleteAndUnique()
    {
        Expect.True(WordOfDayLogic.Words.Length >= 80, "80 words");
        Expect.Equal(WordOfDayLogic.Words.Length, WordOfDayLogic.Words.Select(w => w.Word).Distinct().Count(), "unique words");
        Expect.True(WordOfDayLogic.Words.All(w => w.Definition.Length > 0 && w.Example.Length > 0 && w.Pronunciation.Length > 0), "complete entries");
        Expect.True(WordOfDayLogic.Words.All(w => WordOfDayLogic.Difficulties.Contains(w.Difficulty)), "known difficulties");
        Expect.True(WordOfDayLogic.Words.All(w => WordOfDayLogic.PartsOfSpeech.Contains(w.PartOfSpeech)), "known parts of speech");
        foreach (string difficulty in WordOfDayLogic.Difficulties)
            Expect.True(WordOfDayLogic.Words.Count(w => w.Difficulty == difficulty) >= 10, "enough " + difficulty + " words");
    }

    public static void EveryExampleContainsItsWord()
    {
        foreach (var entry in WordOfDayLogic.Words)
        {
            var found = WordOfDayLogic.FindInExample(entry.Example, entry.Word);
            Expect.True(found is not null, "example for \"" + entry.Word + "\" uses the word");
        }
        var at = WordOfDayLogic.FindInExample("The tenacious puppy refused.", "tenacious")!.Value;
        Expect.Equal("tenacious", "The tenacious puppy refused.".Substring(at.Start, at.Length), "exact match position");
        var inflected = WordOfDayLogic.FindInExample("The magician bamboozled everyone.", "bamboozle")!.Value;
        Expect.Equal("bamboozled", "The magician bamboozled everyone.".Substring(inflected.Start, inflected.Length), "inflected form");
    }

    public static void DifficultyAndPartFiltersExcludeOthers()
    {
        Expect.True(WordOfDayLogic.Filter("hard").All(w => w.Difficulty == "hard"), "hard filter");
        Expect.True(WordOfDayLogic.Filter("easy-medium").All(w => w.Difficulty != "hard"), "combined filter");
        Expect.True(WordOfDayLogic.Filter("medium-hard").All(w => w.Difficulty != "easy"), "other combined filter");
        Expect.Equal(WordOfDayLogic.Words.Length, WordOfDayLogic.Filter("unknown").Count, "fallback filter");
        Expect.True(WordOfDayLogic.Filter("all", "verb").All(w => w.PartOfSpeech == "verb"), "verbs only");
        Expect.True(WordOfDayLogic.Filter("easy", "noun").All(w => w.Difficulty == "easy" && w.PartOfSpeech == "noun"), "easy nouns");
        Expect.True(WordOfDayLogic.Filter("all", "adverb").Count >= 3, "adverbs exist");
        Expect.Equal(WordOfDayLogic.Words.Length, WordOfDayLogic.Filter("all", "all").Count, "no filter");
    }

    public static void SelectionIsStableAndNextChangesWord()
    {
        var pool = WordOfDayLogic.Filter("all");
        var now = new DateTime(2026, 10, 4);
        Expect.Equal(WordOfDayLogic.For(pool, "daily", now, 0), WordOfDayLogic.For(pool, "daily", now.AddHours(12), 0), "stable day");
        Expect.True(WordOfDayLogic.For(pool, "daily", now, 0) != WordOfDayLogic.For(pool, "daily", now, 1), "next word");
        Expect.True(WordOfDayLogic.For(pool, "daily", now, 0) != WordOfDayLogic.For(pool, "daily", now.AddDays(1), 0), "tomorrow's word differs");
        var cycle = Enumerable.Range(0, pool.Count).Select(i => WordOfDayLogic.For(pool, "click", now, i)!.Word).Distinct().Count();
        Expect.Equal(pool.Count, cycle, "clicking visits every word once");
    }

    public static void LabelsAreFriendly()
    {
        Expect.Equal("adj.", WordOfDayLogic.ShortPart("adjective"), "adjective");
        Expect.Equal("adv.", WordOfDayLogic.ShortPart("adverb"), "adverb");
        Expect.Equal("Hard", WordOfDayLogic.Title("hard"), "title case");
        Expect.Equal("", WordOfDayLogic.Title(""), "empty title");
    }
}
