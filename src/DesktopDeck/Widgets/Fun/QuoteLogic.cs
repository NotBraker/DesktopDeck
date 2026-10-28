using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>Quote of the day: the built-in collection, parsing the user's own quotes, and deterministic rotation. Pure logic.</summary>
public static class QuoteLogic
{
    /// <summary>One quote. <see cref="Category"/> is one of <see cref="Categories"/> or "mine".</summary>
    public sealed record Entry(string Text, string Author, string Category);

    public static readonly string[] Categories = ["motivation", "wisdom", "humor", "focus", "calm"];

    /// <summary>Short classic quotes from public-domain sources (old authors, old translations, proverbs).</summary>
    public static readonly Entry[] BuiltIn =
    [
        // Motivation
        new("Well begun is half done.", "Aristotle", "motivation"),
        new("Fortune favors the bold.", "Virgil", "motivation"),
        new("Fall seven times, stand up eight.", "Japanese proverb", "motivation"),
        new("Little by little, one travels far.", "Spanish proverb", "motivation"),
        new("Do what you can, with what you have, where you are.", "Theodore Roosevelt", "motivation"),
        new("Our greatest glory is not in never falling, but in rising every time we fall.", "Oliver Goldsmith", "motivation"),
        new("Great things are not done by impulse, but by a series of small things brought together.", "Vincent van Gogh", "motivation"),
        new("Diligence is the mother of good luck.", "Benjamin Franklin", "motivation"),
        new("Hitch your wagon to a star.", "Ralph Waldo Emerson", "motivation"),
        new("Act well your part; there all the honour lies.", "Alexander Pope", "motivation"),
        new("The harder the conflict, the more glorious the triumph.", "Thomas Paine", "motivation"),
        new("Not failure, but low aim, is crime.", "James Russell Lowell", "motivation"),
        new("Every noble work is at first impossible.", "Thomas Carlyle", "motivation"),
        new("A man's reach should exceed his grasp, or what's a heaven for?", "Robert Browning", "motivation"),
        new("Courage is resistance to fear, mastery of fear, not absence of fear.", "Mark Twain", "motivation"),
        new("No longer talk at all about the kind of man that a good man ought to be, but be such.", "Marcus Aurelius", "motivation"),
        new("Begin at once to live, and count each separate day as a separate life.", "Seneca", "motivation"),
        new("Write it on your heart that every day is the best day in the year.", "Ralph Waldo Emerson", "motivation"),
        new("Light tomorrow with today!", "Elizabeth Barrett Browning", "motivation"),
        new("Hope springs eternal in the human breast.", "Alexander Pope", "motivation"),
        new("Speak softly and carry a big stick; you will go far.", "Theodore Roosevelt", "motivation"),
        new("It is hard to fail, but it is worse never to have tried to succeed.", "Theodore Roosevelt", "motivation"),
        new("All things are difficult before they are easy.", "Thomas Fuller", "motivation"),
        new("A smooth sea never made a skilled sailor.", "English proverb", "motivation"),
        new("Genius is one percent inspiration and ninety-nine percent perspiration.", "Thomas Edison", "motivation"),

        // Wisdom
        new("Know thyself.", "Inscription at Delphi", "wisdom"),
        new("Nothing in excess.", "Inscription at Delphi", "wisdom"),
        new("The unexamined life is not worth living.", "Socrates", "wisdom"),
        new("This above all: to thine own self be true.", "William Shakespeare", "wisdom"),
        new("The fool doth think he is wise, but the wise man knows himself to be a fool.", "William Shakespeare", "wisdom"),
        new("The fault, dear Brutus, is not in our stars, but in ourselves.", "William Shakespeare", "wisdom"),
        new("Knowledge is power.", "Francis Bacon", "wisdom"),
        new("To err is human, to forgive divine.", "Alexander Pope", "wisdom"),
        new("A little learning is a dangerous thing.", "Alexander Pope", "wisdom"),
        new("Experience is the teacher of all things.", "Julius Caesar", "wisdom"),
        new("Philosophy begins in wonder.", "Plato", "wisdom"),
        new("Time is the wisest counselor of all.", "Pericles", "wisdom"),
        new("Doubt is not a pleasant condition, but certainty is absurd.", "Voltaire", "wisdom"),
        new("Honesty is the first chapter in the book of wisdom.", "Thomas Jefferson", "wisdom"),
        new("No man ever steps in the same river twice.", "Heraclitus", "wisdom"),
        new("Where there is no vision, the people perish.", "Proverbs 29:18", "wisdom"),
        new("Pride goeth before destruction, and an haughty spirit before a fall.", "Proverbs 16:18", "wisdom"),
        new("The soul is dyed by the thoughts.", "Marcus Aurelius", "wisdom"),
        new("Reading maketh a full man; conference a ready man; and writing an exact man.", "Francis Bacon", "wisdom"),
        new("To be great is to be misunderstood.", "Ralph Waldo Emerson", "wisdom"),
        new("The years teach much which the days never know.", "Ralph Waldo Emerson", "wisdom"),
        new("Rather than love, than money, than fame, give me truth.", "Henry David Thoreau", "wisdom"),
        new("What do we live for, if it is not to make life less difficult for each other?", "George Eliot", "wisdom"),
        new("The heart has its reasons which reason knows nothing of.", "Blaise Pascal", "wisdom"),
        new("I think, therefore I am.", "René Descartes", "wisdom"),
        new("Early to bed and early to rise, makes a man healthy, wealthy, and wise.", "Benjamin Franklin", "wisdom"),

        // Humor
        new("I can resist everything except temptation.", "Oscar Wilde", "humor"),
        new("There is only one thing in the world worse than being talked about, and that is not being talked about.", "Oscar Wilde", "humor"),
        new("Experience is the name everyone gives to their mistakes.", "Oscar Wilde", "humor"),
        new("To love oneself is the beginning of a lifelong romance.", "Oscar Wilde", "humor"),
        new("I am so clever that sometimes I don't understand a single word of what I am saying.", "Oscar Wilde", "humor"),
        new("The only way to get rid of a temptation is to yield to it.", "Oscar Wilde", "humor"),
        new("Always do right. This will gratify some people and astonish the rest.", "Mark Twain", "humor"),
        new("The report of my death was an exaggeration.", "Mark Twain", "humor"),
        new("Man is the only animal that blushes. Or needs to.", "Mark Twain", "humor"),
        new("Few things are harder to put up with than the annoyance of a good example.", "Mark Twain", "humor"),
        new("Never put off till tomorrow what may be done day after tomorrow just as well.", "Mark Twain", "humor"),
        new("Fish and visitors stink after three days.", "Benjamin Franklin", "humor"),
        new("In this world nothing can be said to be certain, except death and taxes.", "Benjamin Franklin", "humor"),
        new("Who is rich? He that is content. Who is that? Nobody.", "Benjamin Franklin", "humor"),
        new("He that is good for making excuses is seldom good for anything else.", "Benjamin Franklin", "humor"),
        new("Brevity is the soul of wit.", "William Shakespeare", "humor"),
        new("Better a witty fool than a foolish wit.", "William Shakespeare", "humor"),
        new("Even a fool, when he holdeth his peace, is counted wise.", "Proverbs 17:28", "humor"),
        new("Common sense is not so common.", "Voltaire", "humor"),
        new("Hope is a good breakfast, but it is a bad supper.", "Francis Bacon", "humor"),
        new("Do not take life too seriously. You will never get out of it alive.", "Elbert Hubbard", "humor"),
        new("Never explain: your friends do not need it and your enemies will not believe you anyway.", "Elbert Hubbard", "humor"),
        new("I have made this letter longer than usual, only because I have not had the time to make it shorter.", "Blaise Pascal", "humor"),
        new("Fools rush in where angels fear to tread.", "Alexander Pope", "humor"),
        new("Laughter is the sun that drives winter from the human face.", "Victor Hugo", "humor"),

        // Focus
        new("Lost time is never found again.", "Benjamin Franklin", "focus"),
        new("Dost thou love life? Then do not squander time, for that's the stuff life is made of.", "Benjamin Franklin", "focus"),
        new("Have you somewhat to do tomorrow? Do it today.", "Benjamin Franklin", "focus"),
        new("Well done is better than well said.", "Benjamin Franklin", "focus"),
        new("One thing at a time, and that done well, is a very good thing.", "Proverb", "focus"),
        new("Concentration is the secret of strength.", "Ralph Waldo Emerson", "focus"),
        new("He who chases two rabbits catches neither.", "Proverb", "focus"),
        new("The shortest way to do many things is to do only one thing at once.", "Samuel Smiles", "focus"),
        new("Procrastination is the thief of time.", "Edward Young", "focus"),
        new("Whatever is worth doing at all is worth doing well.", "Lord Chesterfield", "focus"),
        new("It is not enough to be busy; so are the ants. The question is: what are we busy about?", "Henry David Thoreau", "focus"),
        new("Simplify, simplify.", "Henry David Thoreau", "focus"),
        new("Make haste slowly.", "Augustus", "focus"),
        new("The man who removes a mountain begins by carrying away small stones.", "Chinese proverb", "focus"),
        new("Patience and diligence, like faith, remove mountains.", "William Penn", "focus"),
        new("While we are postponing, life speeds by.", "Seneca", "focus"),
        new("When a man does not know what harbour he is making for, no wind is the right wind.", "Seneca", "focus"),
        new("The best preparation for good work tomorrow is to do good work today.", "Elbert Hubbard", "focus"),
        new("Nothing is particularly hard if you divide it into small jobs.", "Henry Ford", "focus"),
        new("Thunder is good, thunder is impressive; but it is lightning that does the work.", "Mark Twain", "focus"),
        new("Forever is composed of nows.", "Emily Dickinson", "focus"),

        // Calm
        new("He that can have patience can have what he will.", "Benjamin Franklin", "calm"),
        new("Whatever is begun in anger ends in shame.", "Benjamin Franklin", "calm"),
        new("Content makes poor men rich; discontent makes rich men poor.", "Benjamin Franklin", "calm"),
        new("Rest is not idleness.", "John Lubbock", "calm"),
        new("There is nothing either good or bad, but thinking makes it so.", "William Shakespeare", "calm"),
        new("How poor are they that have not patience!", "William Shakespeare", "calm"),
        new("Sweet are the uses of adversity.", "William Shakespeare", "calm"),
        new("Anger is a short madness.", "Horace", "calm"),
        new("Study to be quiet.", "1 Thessalonians 4:11", "calm"),
        new("A soft answer turneth away wrath.", "Proverbs 15:1", "calm"),
        new("Nothing can bring you peace but yourself.", "Ralph Waldo Emerson", "calm"),
        new("Finish each day and be done with it. You have done what you could.", "Ralph Waldo Emerson", "calm"),
        new("Worry often gives a small thing a big shadow.", "Swedish proverb", "calm"),
        new("The mind is its own place, and in itself can make a heaven of hell, a hell of heaven.", "John Milton", "calm"),
        new("Demand not that events should happen as you wish; but wish them to happen as they do happen, and you will go on well.", "Epictetus", "calm"),
        new("Men are disturbed not by things, but by the views which they take of things.", "Epictetus", "calm"),
        new("Very little is needed to make a happy life.", "Marcus Aurelius", "calm"),
        new("Loss is nothing else but change.", "Marcus Aurelius", "calm"),
        new("We suffer more often in imagination than in reality.", "Seneca", "calm"),
        new("Hope is the thing with feathers that perches in the soul.", "Emily Dickinson", "calm"),
        new("Patience is the companion of wisdom.", "Augustine of Hippo", "calm"),
        new("To travel hopefully is a better thing than to arrive.", "Robert Louis Stevenson", "calm"),
        new("The world is so full of a number of things, I'm sure we should all be as happy as kings.", "Robert Louis Stevenson", "calm"),
        new("Ask yourself whether you are happy, and you cease to be so.", "John Stuart Mill", "calm"),
        new("All the unhappiness of men arises from one single fact, that they cannot stay quietly in their own chamber.", "Blaise Pascal", "calm"),
        new("Patience is bitter, but its fruit is sweet.", "Proverb", "calm"),
        new("The bow too tensely strung is easily broken.", "Publilius Syrus", "calm"),
    ];

    private static readonly string[] Separators = [" — ", " – ", " -- ", " ~ ", " - ", "—", "–", "~"];

    /// <summary>Parses one "quote — author" line (also " - ", " -- ", "~"). Strips surrounding quote marks. Null for blank lines.</summary>
    public static Entry? ParseLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        string text = line.Trim(), author = "";
        foreach (var separator in Separators)
        {
            int at = text.LastIndexOf(separator, StringComparison.Ordinal);
            if (at <= 0) continue;
            author = text[(at + separator.Length)..].Trim();
            text = text[..at].Trim();
            break;
        }
        text = text.Trim().Trim('"', '“', '”', '«', '»', '\'', '‘', '’').Trim();
        return text.Length == 0 ? null : new Entry(text, author, "mine");
    }

    /// <summary>The pool to rotate through: built-in quotes in the chosen categories, the user's own, or both.</summary>
    public static IReadOnlyList<Entry> Pool(string source, IEnumerable<string> categories, IEnumerable<string> userLines)
    {
        var chosen = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
        var mine = userLines.Select(ParseLine).OfType<Entry>().ToList();
        var builtIn = BuiltIn.Where(q => chosen.Contains(q.Category)).ToList();
        return source switch
        {
            "mine" => mine,
            "both" => builtIn.Concat(mine).ToList(),
            _ => builtIn,
        };
    }

    /// <summary>
    /// The rotation step for a moment: days since 2000-01-01 ("daily"), hours since then ("hourly"), or 0 ("click":
    /// only the click offset moves it). Uses local wall-clock time so every copy on this PC agrees.
    /// </summary>
    public static long Period(string rotation, DateTime now)
    {
        var epoch = new DateTime(2000, 1, 1);
        return rotation switch
        {
            "hourly" => (long)Math.Floor((now - epoch).TotalHours),
            "click" => 0,
            _ => (long)Math.Floor((now.Date - epoch).TotalDays),
        };
    }

    /// <summary>
    /// Index into a list of <paramref name="count"/> items for rotation step <paramref name="step"/>: every block of
    /// <paramref name="count"/> steps visits each item exactly once in a shuffled order that is the same on every PC
    /// and every run (own PRNG, never string hashes). <paramref name="salt"/> lets different widgets shuffle differently.
    /// </summary>
    public static int Pick(long step, int count, int salt = 0)
    {
        if (count <= 0) return -1;
        long block = FloorDiv(step, count);
        int position = (int)(step - block * count);
        var order = Shuffle(count, unchecked((ulong)block * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)salt * 0xBF58476D1CE4E5B9UL ^ 0x5DEECE66DUL));
        return order[position];
    }

    /// <summary>The quote for a moment: <paramref name="offset"/> is the number of "next" clicks.</summary>
    public static Entry? For(IReadOnlyList<Entry> pool, string rotation, DateTime now, long offset)
    {
        int index = Pick(Period(rotation, now) + offset, pool.Count, 17);
        return index < 0 ? null : pool[index];
    }

    /// <summary>Wraps a quote in the chosen marks: "curly", "straight", "guillemets", "none" (or "big", drawn by the widget).</summary>
    public static string Wrap(string text, string marks) => marks switch
    {
        "curly" => "“" + text + "”",
        "straight" => "\"" + text + "\"",
        "guillemets" => "« " + text + " »",
        "corner" => "「" + text + "」",
        _ => text,
    };

    // ---------- Click offset (resets when the rotation period moves on) ----------

    /// <summary>Stores how many "next" clicks were made during <paramref name="period"/> as "period:offset".</summary>
    public static string WriteOffset(long period, long offset)
        => period.ToString(CultureInfo.InvariantCulture) + ":" + offset.ToString(CultureInfo.InvariantCulture);

    /// <summary>The saved click offset, or 0 when it was made in a different period (a new day starts at today's entry).</summary>
    public static long ReadOffset(string? stored, long period)
    {
        var parts = (stored ?? "").Split(':');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long savedPeriod)
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long offset)
            || savedPeriod != period) return 0;
        return Math.Clamp(offset, -1_000_000, 1_000_000);
    }

    // ---------- Fitting text ----------

    /// <summary>Roughly how tall <paramref name="text"/> is when word-wrapped into <paramref name="width"/> at <paramref name="size"/>.</summary>
    public static double EstimateHeight(string text, double size, double width)
    {
        int perLine = Math.Max(1, (int)(width / (size * 0.54)));
        int lines = 1, used = 0;
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            int length = word.Length;
            if (used == 0) used = length;
            else if (used + 1 + length <= perLine) used += 1 + length;
            else { lines++; used = length; }
            while (used > perLine) { lines++; used -= perLine; }
        }
        return lines * size * 1.38;
    }

    /// <summary>The largest font size from <paramref name="max"/> down to <paramref name="min"/> whose wrapped text fits the height.</summary>
    public static double FitFontSize(string text, double max, double min, double width, double height)
    {
        for (double size = max; size > min; size -= 0.5)
            if (EstimateHeight(text, size, width) <= height) return size;
        return min;
    }

    public static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    // ---------- Deterministic shuffle (SplitMix64 + Fisher–Yates) ----------

    internal static int[] Shuffle(int count, ulong seed)
    {
        var order = Enumerable.Range(0, count).ToArray();
        ulong state = seed;
        for (int i = count - 1; i > 0; i--)
        {
            int j = (int)(Next(ref state) % (ulong)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    private static ulong Next(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}
