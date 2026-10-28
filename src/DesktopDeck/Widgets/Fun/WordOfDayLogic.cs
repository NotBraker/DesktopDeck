using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>Word of the day: a built-in list of interesting English words and deterministic daily rotation. Pure logic.</summary>
public static class WordOfDayLogic
{
    /// <summary>One word. <see cref="Difficulty"/> is "easy", "medium" or "hard".</summary>
    public sealed record Entry(string Word, string PartOfSpeech, string Pronunciation, string Definition, string Example, string Difficulty);

    public static readonly string[] Difficulties = ["easy", "medium", "hard"];

    public static readonly Entry[] Words =
    [
        new("serendipity", "noun", "ser-uhn-DIP-ih-tee", "Finding something good or useful without looking for it.", "By pure serendipity, she met her future business partner in a train delay.", "medium"),
        new("ephemeral", "adjective", "ih-FEM-er-uhl", "Lasting for only a very short time.", "The ephemeral beauty of the cherry blossoms drew crowds every spring.", "medium"),
        new("petrichor", "noun", "PET-rih-kor", "The earthy smell in the air when rain falls on dry ground.", "After weeks of drought, the petrichor hit us the moment the storm began.", "hard"),
        new("luminous", "adjective", "LOO-muh-nuhs", "Giving off light; bright and glowing.", "The luminous dial let him read his watch in the dark.", "easy"),
        new("mellifluous", "adjective", "muh-LIF-loo-uhs", "Sweet, smooth and pleasant to hear.", "Her mellifluous voice made even the safety briefing sound lovely.", "hard"),
        new("quixotic", "adjective", "kwik-SOT-ik", "Hopelessly idealistic; noble but not practical.", "His quixotic plan to plant a tree for every resident took him decades.", "hard"),
        new("ubiquitous", "adjective", "yoo-BIK-wih-tuhs", "Seeming to be everywhere at the same time.", "Phones have become ubiquitous on every bus and in every café.", "medium"),
        new("resilient", "adjective", "rih-ZIL-yuhnt", "Able to recover quickly after a setback.", "The resilient team bounced back after losing the first match.", "easy"),
        new("eloquent", "adjective", "EL-uh-kwuhnt", "Expressing ideas clearly, fluently and persuasively.", "Her eloquent speech convinced the council to save the library.", "easy"),
        new("candor", "noun", "KAN-der", "Open, frank honesty in what you say.", "I appreciated his candor about the project's real deadline.", "medium"),
        new("zenith", "noun", "ZEE-nith", "The highest point or peak of something.", "At the zenith of her career, she toured forty cities in a year.", "medium"),
        new("nadir", "noun", "NAY-deer", "The lowest point of something.", "Losing the final was the nadir of the club's season.", "hard"),
        new("halcyon", "adjective", "HAL-see-uhn", "Calm, happy and peaceful, usually describing a time in the past.", "They often talked about the halcyon summers at their grandparents' farm.", "hard"),
        new("wanderlust", "noun", "WON-der-lust", "A strong desire to travel and explore.", "A bout of wanderlust sent him backpacking across three continents.", "easy"),
        new("sanguine", "adjective", "SANG-gwin", "Cheerfully optimistic, even in a difficult situation.", "Despite the rain forecast, she stayed sanguine about the picnic.", "hard"),
        new("gregarious", "adjective", "grih-GAIR-ee-uhs", "Fond of company; sociable.", "Our gregarious neighbor knows everyone on the street by name.", "medium"),
        new("laconic", "adjective", "luh-KON-ik", "Using very few words.", "His laconic reply was simply, \"Fine.\"", "hard"),
        new("meticulous", "adjective", "muh-TIK-yuh-luhs", "Showing careful attention to every detail.", "The meticulous chef weighed each spice to the gram.", "easy"),
        new("diligent", "adjective", "DIL-ih-juhnt", "Careful, steady and hardworking.", "Diligent practice turned her shaky scales into smooth runs.", "easy"),
        new("pensive", "adjective", "PEN-siv", "Quietly and deeply thoughtful, sometimes a little sad.", "He grew pensive as the train pulled out of his hometown.", "medium"),
        new("ineffable", "adjective", "in-EF-uh-buhl", "Too great or too strange to be described in words.", "Standing under the northern lights filled her with ineffable joy.", "hard"),
        new("vivacious", "adjective", "vih-VAY-shuhs", "Lively, spirited and full of energy.", "The vivacious host kept the party laughing all night.", "medium"),
        new("benevolent", "adjective", "buh-NEV-uh-luhnt", "Kind and wanting to do good for others.", "A benevolent stranger paid for everyone's coffee that morning.", "medium"),
        new("tenacious", "adjective", "tuh-NAY-shuhs", "Holding on firmly; very determined not to give up.", "The tenacious puppy refused to let go of the rope toy.", "medium"),
        new("aplomb", "noun", "uh-PLOM", "Calm self-confidence, especially under pressure.", "She handled the tricky interview question with aplomb.", "hard"),
        new("cacophony", "noun", "kuh-KOF-uh-nee", "A harsh, jarring mix of loud sounds.", "The cacophony of car horns made it hard to think.", "medium"),
        new("euphoria", "noun", "yoo-FOR-ee-uh", "A feeling of intense happiness and excitement.", "Euphoria swept the stadium when the winning goal went in.", "easy"),
        new("nostalgia", "noun", "no-STAL-juh", "A fond, wistful longing for the past.", "The smell of popcorn filled him with nostalgia for old movie nights.", "easy"),
        new("paradox", "noun", "PAIR-uh-doks", "Something that seems to contradict itself but may still be true.", "It's a paradox that the busiest people often find the most free time.", "easy"),
        new("pragmatic", "adjective", "prag-MAT-ik", "Dealing with problems in a sensible, practical way.", "We took a pragmatic approach and fixed the worst bug first.", "medium"),
        new("ambivalent", "adjective", "am-BIV-uh-luhnt", "Having mixed or contradictory feelings about something.", "She felt ambivalent about moving: excited, but sad to leave.", "medium"),
        new("lethargic", "adjective", "luh-THAR-jik", "Sluggish, drowsy and lacking energy.", "The heat made everyone lethargic by mid-afternoon.", "medium"),
        new("ebullient", "adjective", "ih-BUL-yuhnt", "Overflowing with cheerful excitement.", "The ebullient fans sang all the way home.", "hard"),
        new("obfuscate", "verb", "OB-fuh-skayt", "To make something deliberately unclear or confusing.", "The long contract seemed designed to obfuscate the fees.", "hard"),
        new("perfunctory", "adjective", "per-FUNK-tuh-ree", "Done quickly as a routine, without real care or interest.", "He gave the dishes a perfunctory rinse and left them to dry.", "hard"),
        new("sagacious", "adjective", "suh-GAY-shuhs", "Wise and showing sound judgment.", "The sagacious old captain read the clouds better than any forecast.", "hard"),
        new("verbose", "adjective", "ver-BOHS", "Using more words than are needed.", "The verbose manual took three pages to explain the on switch.", "medium"),
        new("whimsical", "adjective", "WIM-zih-kuhl", "Playful and fanciful in an unusual way.", "The café was decorated with whimsical teapots hanging from the ceiling.", "easy"),
        new("zephyr", "noun", "ZEF-er", "A soft, gentle breeze.", "A warm zephyr drifted through the open window.", "medium"),
        new("epiphany", "noun", "ih-PIF-uh-nee", "A sudden moment of clear understanding.", "Halfway through the shower, he had an epiphany about the puzzle.", "easy"),
        new("hubris", "noun", "HYOO-bris", "Excessive pride or self-confidence.", "Skipping the rehearsal out of hubris cost them the show.", "medium"),
        new("idyllic", "adjective", "eye-DIL-ik", "Extremely peaceful, happy and picturesque.", "They rented an idyllic cottage beside a quiet lake.", "medium"),
        new("juxtapose", "verb", "JUK-stuh-pohz", "To place things side by side to compare or contrast them.", "The exhibit juxtaposes old photographs with modern views of the same streets.", "medium"),
        new("kerfuffle", "noun", "ker-FUF-uhl", "A fuss or commotion, usually over something minor.", "There was a kerfuffle over who had taken the last parking spot.", "medium"),
        new("lackadaisical", "adjective", "lak-uh-DAY-zih-kuhl", "Lacking enthusiasm, effort or care.", "His lackadaisical packing meant he forgot his toothbrush again.", "medium"),
        new("magnanimous", "adjective", "mag-NAN-uh-muhs", "Generous and forgiving, especially toward a rival.", "The magnanimous winner thanked her opponent first.", "hard"),
        new("nonchalant", "adjective", "non-shuh-LAHNT", "Calm and casual, seeming not to care.", "He gave a nonchalant shrug, as if winning were nothing.", "medium"),
        new("onomatopoeia", "noun", "on-uh-mat-uh-PEE-uh", "A word that imitates the sound it describes.", "\"Buzz\" and \"sizzle\" are classic examples of onomatopoeia.", "hard"),
        new("panacea", "noun", "pan-uh-SEE-uh", "A supposed cure for every problem.", "A new app is not a panacea for a messy schedule.", "hard"),
        new("quintessential", "adjective", "kwin-tuh-SEN-shuhl", "Being the most perfect, typical example of something.", "Rain, tea and a good book make a quintessential lazy Sunday.", "medium"),
        new("reverie", "noun", "REV-uh-ree", "A pleasant, dreamy state of thought.", "The kettle's whistle broke her reverie.", "medium"),
        new("solace", "noun", "SOL-is", "Comfort in a time of sadness or worry.", "He found solace in long walks by the river.", "medium"),
        new("tranquil", "adjective", "TRANG-kwil", "Free from disturbance; calm and quiet.", "The tranquil garden was the perfect place to read.", "easy"),
        new("umbrage", "noun", "UM-brij", "A feeling of offense or annoyance.", "She took umbrage at being called \"the new kid\" after ten years.", "hard"),
        new("vicarious", "adjective", "vy-KAIR-ee-uhs", "Felt through imagining someone else's experience.", "He got a vicarious thrill from his sister's travel stories.", "hard"),
        new("wistful", "adjective", "WIST-fuhl", "Gently sad and longing.", "She gave a wistful smile as she looked at the old photos.", "medium"),
        new("zealous", "adjective", "ZEL-uhs", "Full of eager, energetic enthusiasm.", "The zealous volunteer labeled every box in the warehouse.", "medium"),
        new("alacrity", "noun", "uh-LAK-rih-tee", "Cheerful readiness and speed.", "The kids cleared the table with surprising alacrity when dessert was mentioned.", "hard"),
        new("bucolic", "adjective", "byoo-KOL-ik", "Relating to the pleasant side of country life.", "The painting showed a bucolic scene of sheep and stone walls.", "hard"),
        new("clandestine", "adjective", "klan-DES-tin", "Done secretly, often because it is not allowed.", "They held a clandestine meeting to plan the surprise party.", "medium"),
        new("dauntless", "adjective", "DAWNT-lis", "Fearless and determined.", "The dauntless climbers pressed on through the storm.", "medium"),
        new("effervescent", "adjective", "ef-er-VES-uhnt", "Bubbly; lively and enthusiastic.", "Her effervescent laugh could brighten any meeting.", "medium"),
        new("fastidious", "adjective", "fa-STID-ee-uhs", "Very concerned about accuracy, cleanliness and detail.", "The fastidious librarian shelved every book by height as well.", "hard"),
        new("garrulous", "adjective", "GAIR-uh-luhs", "Excessively talkative, especially about trivial things.", "The garrulous taxi driver narrated every street we passed.", "hard"),
        new("hiatus", "noun", "hy-AY-tuhs", "A pause or break in an activity.", "The band returned after a five-year hiatus.", "medium"),
        new("impetus", "noun", "IM-pih-tuhs", "The force or motivation that makes something happen.", "The broken heater was the impetus for finally renovating.", "medium"),
        new("jovial", "adjective", "JOH-vee-uhl", "Cheerful, friendly and good-humored.", "The jovial baker greeted every customer with a joke.", "easy"),
        new("kindle", "verb", "KIN-duhl", "To light a fire; to stir up a feeling or interest.", "A single science class kindled her love of astronomy.", "easy"),
        new("labyrinth", "noun", "LAB-uh-rinth", "A complicated network of paths; a maze.", "The old town was a labyrinth of narrow alleys.", "easy"),
        new("mundane", "adjective", "mun-DAYN", "Ordinary, everyday and unexciting.", "Even mundane chores go faster with good music.", "easy"),
        new("nuance", "noun", "NOO-ahns", "A subtle difference in meaning, tone or feeling.", "Translators work hard to keep every nuance of the original.", "medium"),
        new("oblivious", "adjective", "uh-BLIV-ee-uhs", "Not aware of what is happening around you.", "Wearing headphones, he was oblivious to the doorbell.", "easy"),
        new("plethora", "noun", "PLETH-er-uh", "A large or excessive amount of something.", "The menu offered a plethora of pancake toppings.", "medium"),
        new("quandary", "noun", "KWON-duh-ree", "A state of uncertainty about what to do.", "Two great job offers left her in a happy quandary.", "medium"),
        new("resplendent", "adjective", "rih-SPLEN-duhnt", "Dazzlingly bright and impressive.", "The hall was resplendent with gold lanterns.", "hard"),
        new("scrumptious", "adjective", "SKRUMP-shuhs", "Extremely tasty; delicious.", "Grandma's scrumptious pie vanished in minutes.", "easy"),
        new("tenuous", "adjective", "TEN-yoo-uhs", "Very weak, thin or uncertain.", "The link between the two events was tenuous at best.", "hard"),
        new("unwieldy", "adjective", "un-WEEL-dee", "Hard to carry or manage because of its size or shape.", "The unwieldy couch would not fit through the stairwell.", "medium"),
        new("vestige", "noun", "VES-tij", "A small remaining trace of something that once existed.", "A crumbling arch is the last vestige of the old castle.", "hard"),
        new("wane", "verb", "WAYN", "To gradually become smaller or weaker.", "Interest in the fad began to wane after a month.", "easy"),
        new("yearn", "verb", "YURN", "To have a strong, often sad longing.", "In winter, she would yearn for long beach days.", "easy"),
        new("zest", "noun", "ZEST", "Great enthusiasm and energy; also the outer peel of citrus.", "He tackled every new hobby with zest.", "easy"),
        new("bamboozle", "verb", "bam-BOO-zuhl", "To trick, fool or confuse someone.", "The magician bamboozled the whole audience with a single card.", "easy"),
        new("candid", "adjective", "KAN-did", "Honest and direct; also unposed, as in a photo.", "The best wedding pictures were the candid ones.", "easy"),
        new("dapper", "adjective", "DAP-er", "Neat, trim and stylish in dress.", "He looked dapper in his new suit and bow tie.", "easy"),
        new("gumption", "noun", "GUMP-shuhn", "Practical good sense combined with initiative.", "It took gumption to open a bakery at nineteen.", "medium"),
        new("persnickety", "adjective", "per-SNIK-ih-tee", "Fussy about small details.", "My persnickety cat will only drink from a running tap.", "medium"),
        new("sesquipedalian", "adjective", "ses-kwih-puh-DAY-lee-uhn", "Fond of using long words; having many syllables.", "The professor's sesquipedalian lectures sent us to the dictionary.", "hard"),
        new("defenestrate", "verb", "dee-FEN-uh-strayt", "To throw someone or something out of a window.", "After the tenth crash, he was tempted to defenestrate his laptop.", "hard"),
        new("flabbergasted", "adjective", "FLAB-er-gas-tid", "Extremely surprised; astonished.", "She was flabbergasted to find her old diary in a thrift shop.", "easy"),
        new("hullabaloo", "noun", "HUL-uh-buh-loo", "A noisy commotion or fuss.", "The escaped goat caused quite a hullabaloo at the fair.", "easy"),
        new("cogent", "adjective", "KOH-juhnt", "Clear, logical and convincing.", "She made a cogent case for a four-day week.", "hard"),
        new("lucid", "adjective", "LOO-sid", "Clear and easy to understand.", "The guide gave a lucid explanation of how tides work.", "easy"),
        new("serene", "adjective", "suh-REEN", "Calm, peaceful and untroubled.", "The lake was serene at dawn, without a single ripple.", "easy"),
        new("gallivant", "verb", "GAL-ih-vant", "To go around from place to place in search of fun.", "They spent the summer gallivanting around the coast.", "medium"),
        new("brouhaha", "noun", "BROO-hah-hah", "A noisy and overexcited reaction to something.", "The new parking rules caused a brouhaha at the town meeting.", "medium"),
        new("eagerly", "adverb", "EE-ger-lee", "With keen, excited interest.", "The kids eagerly tore open the box.", "easy"),
        new("gingerly", "adverb", "JIN-jer-lee", "In a very careful, cautious way.", "He gingerly stepped onto the frozen pond.", "medium"),
        new("ostensibly", "adverb", "os-TEN-sih-blee", "Apparently or supposedly, though maybe not truly.", "She was ostensibly working, but she was really daydreaming.", "hard"),
        new("assiduously", "adverb", "uh-SID-yoo-uhs-lee", "With steady, careful and persistent effort.", "He assiduously watered the seedlings every morning.", "hard"),
        new("inexorably", "adverb", "in-EK-ser-uh-blee", "In a way that cannot be stopped or changed.", "The tide crept inexorably up the beach.", "hard"),
        new("ruminate", "verb", "ROO-muh-nayt", "To think about something slowly and deeply.", "She liked to ruminate on big ideas during a long walk.", "medium"),
        new("ameliorate", "verb", "uh-MEEL-yuh-rayt", "To make a bad situation somewhat better.", "New bike lanes helped ameliorate the morning traffic.", "hard"),
    ];

    /// <summary>"easy" → "Easy", for badges.</summary>
    public static string Title(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>The words matching a difficulty filter: "all", "easy", "medium", "hard", "easy-medium" or "medium-hard".</summary>
    public static IReadOnlyList<Entry> Filter(string difficulty, string partOfSpeech = "all")
    {
        IEnumerable<Entry> pool = difficulty switch
        {
            "easy" or "medium" or "hard" => Words.Where(w => w.Difficulty == difficulty),
            "easy-medium" => Words.Where(w => w.Difficulty != "hard"),
            "medium-hard" => Words.Where(w => w.Difficulty != "easy"),
            _ => Words,
        };
        if (PartsOfSpeech.Contains(partOfSpeech) && partOfSpeech != "all") pool = pool.Where(w => w.PartOfSpeech == partOfSpeech);
        var list = pool.ToList();
        return list.Count > 0 ? list : Words;
    }

    public static readonly string[] PartsOfSpeech = ["all", "noun", "adjective", "verb", "adverb"];

    /// <summary>The word for a moment and rotation ("daily", "hourly", "click"), after <paramref name="offset"/> "next" clicks.</summary>
    public static Entry? For(IReadOnlyList<Entry> pool, string rotation, DateTime now, long offset)
    {
        int index = QuoteLogic.Pick(QuoteLogic.Period(rotation, now) + offset, pool.Count, 29);
        return index < 0 ? null : pool[index];
    }

    /// <summary>Where the word (or a form of it, such as "kindled") first appears in its example: start and length, or null.</summary>
    public static (int Start, int Length)? FindInExample(string example, string word)
    {
        string lower = word.ToLowerInvariant();
        string stem = lower.Length > 6 ? lower[..^2] : lower.Length > 4 ? lower[..^1] : lower;
        int i = 0;
        while (i < example.Length)
        {
            if (!char.IsLetter(example[i])) { i++; continue; }
            int start = i;
            while (i < example.Length && (char.IsLetter(example[i]) || example[i] == '\'')) i++;
            if (example[start..i].ToLowerInvariant().StartsWith(stem, StringComparison.Ordinal)) return (start, i - start);
        }
        return null;
    }

    /// <summary>Short part-of-speech label: "n.", "adj.", "v.", "adv.".</summary>
    public static string ShortPart(string partOfSpeech) => partOfSpeech switch
    {
        "noun" => "n.",
        "adjective" => "adj.",
        "verb" => "v.",
        "adverb" => "adv.",
        _ => partOfSpeech,
    };
}
