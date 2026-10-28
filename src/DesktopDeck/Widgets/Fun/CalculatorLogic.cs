using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesktopDeck.Widgets;

/// <summary>
/// The calculator engine: immediate execution like a desk calculator (1 + 2 × 3 = 9), %, ±, memory and a history
/// tape. Numbers are decimals; the entry is kept with "." internally and only localized for display. Pure logic.
/// </summary>
public static class CalculatorLogic
{
    public const int MaxDigits = 16;

    /// <summary>
    /// Everything the calculator remembers. <see cref="Display"/> is an invariant number string (possibly partial while
    /// typing, e.g. "12." or "-0"). <see cref="Typing"/>: digits append to it. <see cref="Entered"/>: it holds a new
    /// operand for the pending operator <see cref="Op"/> ("+", "-", "*", "/" or "").
    /// </summary>
    public sealed record State
    {
        public string Display { get; init; } = "0";
        public bool Typing { get; init; }
        public bool Entered { get; init; }
        public decimal Accumulator { get; init; }
        public string Op { get; init; } = "";
        public string LastOp { get; init; } = "";
        public decimal LastOperand { get; init; }
        /// <summary>The finished calculation shown above the result after "=", e.g. "12|+|3" (invariant).</summary>
        public string Done { get; init; } = "";
        public decimal Memory { get; init; }
        public bool HasMemory { get; init; }
        /// <summary>Non-empty when the last operation failed ("Cannot divide by zero", "Overflow", …).</summary>
        public string Error { get; init; } = "";

        public decimal Value => ParseValue(Display);
    }

    /// <summary>One finished calculation for the history tape.</summary>
    public sealed record Calculation(decimal Left, string Op, decimal Right, decimal Result);

    public static readonly State Initial = new();

    /// <summary>
    /// Applies one key: digits "0"–"9", ".", "+", "-", "*", "/", "=", "%", "neg", "C", "CE", "back", "MC", "MR",
    /// "M+", "M-". Returns the new state and, after "=", the calculation for the history tape.
    /// </summary>
    public static (State State, Calculation? Done) Press(State s, string key)
    {
        if (s.Error.Length > 0)
        {
            // After an error only clearing and new numbers make sense.
            var cleared = Initial with { Memory = s.Memory, HasMemory = s.HasMemory };
            if (key is "C" or "CE" or "back") return (cleared, null);
            if (IsDigit(key) || key == ".") return Press(cleared, key);
            if (key is "MC" or "MR") return Press(cleared, key);
            return (s, null);
        }

        switch (key)
        {
            case "0" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" or ".":
                return (Digit(s, key), null);
            case "+" or "-" or "*" or "/":
                return (Operator(s, key), null);
            case "=":
                return Equals(s);
            case "%":
            {
                decimal x = s.Value;
                decimal value; try { value = s.Op is "+" or "-" ? checked(s.Accumulator * x / 100m) : x / 100m; } catch (OverflowException) { return (s with { Error = "Overflow" }, null); }
                return (s with { Display = Raw(value), Typing = false, Entered = true, Done = "" }, null);
            }
            case "neg":
                if (s.Typing)
                    return (s with { Display = s.Display.StartsWith('-') ? s.Display[1..] : "-" + s.Display }, null);
                try { return (s with { Display = Raw(-s.Value), Entered = true, Done = "" }, null); } catch (OverflowException) { return (s with { Error = "Overflow" }, null); }
            case "C":
                return (Initial with { Memory = s.Memory, HasMemory = s.HasMemory }, null);
            case "CE":
                return (s with { Display = "0", Typing = false, Entered = s.Op.Length > 0, Done = "" }, null);
            case "back":
                if (!s.Typing) return (s.Done.Length > 0 ? s with { Done = "" } : s, null);
                string shorter = s.Display[..^1];
                if (shorter is "" or "-") shorter = "0";
                return (s with { Display = shorter }, null);
            case "MC":
                return (s with { Memory = 0, HasMemory = false }, null);
            case "MR":
                return (s with { Display = Raw(s.Memory), Typing = false, Entered = true, Done = "" }, null);
            case "M+" or "M-":
            {
                try
                {
                    decimal memory = key == "M+" ? s.Memory + s.Value : s.Memory - s.Value;
                    return (s with { Memory = memory, HasMemory = true, Typing = false }, null);
                }
                catch (OverflowException) { return (s with { Error = "Overflow" }, null); }
            }
            default:
                return (s, null);
        }
    }

    /// <summary>Loads a number (for example a result from the tape) as the operand being entered.</summary>
    public static State Recall(State s, decimal value)
        => s.Error.Length > 0 ? s : s with { Display = Raw(value), Typing = false, Entered = true, Done = "" };

    /// <summary>The shown result as plain text for pasting: localized decimal mark, no thousands separators.</summary>
    public static string PlainResult(State s, int decimals, NumberFormatInfo format)
        => s.Error.Length > 0 ? "" : Format(s.Value, decimals, false, format);

    /// <summary>Applies a sequence of keys (handy for tests).</summary>
    public static State Run(State s, params string[] keys)
    {
        foreach (var key in keys) s = Press(s, key).State;
        return s;
    }

    private static bool IsDigit(string key) => key.Length == 1 && key[0] is >= '0' and <= '9';

    private static int DigitCount(string text) => text.Count(char.IsAsciiDigit);

    private static State Digit(State s, string key)
    {
        if (!s.Typing)
            return s with { Display = key == "." ? "0." : key, Typing = true, Entered = true, Done = "" };
        if (key == ".") return s.Display.Contains('.') ? s : s with { Display = s.Display + "." };
        if (DigitCount(s.Display) >= MaxDigits) return s;
        if (s.Display == "0") return s with { Display = key };
        if (s.Display == "-0") return s with { Display = "-" + key };
        return s with { Display = s.Display + key };
    }

    private static State Operator(State s, string op)
    {
        if (s.Op.Length > 0 && !s.Entered) return s with { Op = op, Done = "" };
        string error = ""; decimal left = s.Op.Length > 0 ? Apply(s.Accumulator, s.Op, s.Value, out error) : s.Value;
        if (s.Op.Length > 0 && error.Length > 0) return s with { Error = error, Typing = false };
        return s with { Accumulator = left, Op = op, Display = Raw(left), Typing = false, Entered = false, Done = "" };
    }

    private static (State, Calculation?) Equals(State s)
    {
        string op;
        decimal left, right;
        if (s.Op.Length > 0) { op = s.Op; left = s.Accumulator; right = s.Value; }
        else if (s.LastOp.Length > 0) { op = s.LastOp; left = s.Value; right = s.LastOperand; }
        else return (s with { Typing = false }, null);
        decimal result = Apply(left, op, right, out string error);
        if (error.Length > 0) return (s with { Error = error, Typing = false, Op = "" }, null);
        var state = s with
        {
            Display = Raw(result), Typing = false, Entered = false, Op = "", Accumulator = 0,
            LastOp = op, LastOperand = right, Done = Raw(left) + "|" + op + "|" + Raw(right),
        };
        return (state, new Calculation(left, op, right, result));
    }

    /// <summary>left op right, with an error message instead of exceptions.</summary>
    public static decimal Apply(decimal left, string op, decimal right, out string error)
    {
        error = "";
        try
        {
            switch (op)
            {
                case "+": return left + right;
                case "-": return left - right;
                case "*": return left * right;
                case "/":
                    if (right == 0) { error = left == 0 ? "Result is undefined" : "Cannot divide by zero"; return 0; }
                    return left / right;
                default: return right;
            }
        }
        catch (OverflowException) { error = "Overflow"; return 0; }
    }

    /// <summary>An invariant, trailing-zero-free number string.</summary>
    public static string Raw(decimal value)
    {
        string text = (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }

    public static decimal ParseValue(string text)
    {
        string t = text.Trim();
        if (t.EndsWith('.')) t = t[..^1];
        return decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    // ---------- Display ----------

    /// <summary>
    /// A result for display. <paramref name="decimals"/> −1 = automatic (up to 10 places, about 15 significant digits,
    /// trailing zeros trimmed); otherwise fixed places. Very large numbers switch to scientific notation.
    /// </summary>
    public static string Format(decimal value, int decimals, bool thousands, NumberFormatInfo format)
    {
        decimal abs = Math.Abs(value);
        if (abs >= 1e15m)
            return ((double)value).ToString("0.#########E+0", format);
        string group = thousands ? "#,##0" : "0";
        if (decimals >= 0)
        {
            decimal rounded = Math.Round(value, Math.Min(decimals, 20), MidpointRounding.AwayFromZero);
            return Clean(rounded.ToString(group + (decimals > 0 ? "." + new string('0', decimals) : ""), format), format);
        }
        int integerDigits = abs < 1 ? 1 : (int)Math.Floor(Math.Log10((double)abs)) + 1;
        int places = Math.Clamp(15 - integerDigits, 0, 10);
        decimal r = Math.Round(value, places, MidpointRounding.AwayFromZero);
        if (r == 0 && value != 0)
            return ((double)value).ToString("0.#####E+0", format);
        return Clean(r.ToString(group + (places > 0 ? "." + new string('#', places) : ""), format), format);
    }

    private static string Clean(string text, NumberFormatInfo format)
        => text.StartsWith(format.NegativeSign, StringComparison.Ordinal) && text[format.NegativeSign.Length..].All(c => !char.IsAsciiDigit(c) || c == '0')
            ? text[format.NegativeSign.Length..]
            : text;

    /// <summary>The number being typed, localized and grouped but otherwise exactly as typed ("1,234." stays).</summary>
    public static string FormatEntry(string raw, bool thousands, NumberFormatInfo format)
    {
        bool negative = raw.StartsWith('-');
        string body = negative ? raw[1..] : raw;
        int dot = body.IndexOf('.');
        string whole = dot >= 0 ? body[..dot] : body, fraction = dot >= 0 ? body[(dot + 1)..] : "";
        if (whole.Length == 0) whole = "0";
        if (thousands && whole.Length > 3)
        {
            string separator = format.NumberGroupSeparator;
            var parts = new List<string>();
            for (int end = whole.Length; end > 0; end -= 3) parts.Insert(0, whole[Math.Max(0, end - 3)..end]);
            whole = string.Join(separator, parts);
        }
        return (negative ? format.NegativeSign : "") + whole + (dot >= 0 ? format.NumberDecimalSeparator + fraction : "");
    }

    /// <summary>What the main display shows for a state.</summary>
    public static string Screen(State s, int decimals, bool thousands, NumberFormatInfo format)
    {
        if (s.Error.Length > 0) return s.Error;
        return s.Typing ? FormatEntry(s.Display, thousands, format) : Format(s.Value, decimals, thousands, format);
    }

    /// <summary>The small line above the result: "12 +" while an operator is pending, "12 + 3 =" after equals.</summary>
    public static string Expression(State s, int decimals, bool thousands, NumberFormatInfo format)
    {
        if (s.Op.Length > 0) return Format(s.Accumulator, decimals, thousands, format) + " " + Symbol(s.Op);
        if (s.Done.Split('|') is [var left, var op, var right])
            return $"{Format(ParseValue(left), decimals, thousands, format)} {Symbol(op)} {Format(ParseValue(right), decimals, thousands, format)} =";
        return "";
    }

    public static string Symbol(string op) => op switch { "+" => "+", "-" => "−", "*" => "×", "/" => "÷", _ => op };

    // ---------- Storage (invariant) ----------

    public static string EncodeCalculation(Calculation c) => $"{Raw(c.Left)}|{c.Op}|{Raw(c.Right)}|{Raw(c.Result)}";

    public static Calculation? DecodeCalculation(string line)
    {
        var parts = line.Split('|');
        if (parts.Length != 4 || parts[1] is not ("+" or "-" or "*" or "/")) return null;
        return new Calculation(ParseValue(parts[0]), parts[1], ParseValue(parts[2]), ParseValue(parts[3]));
    }

    public static string FormatCalculation(Calculation c, int decimals, bool thousands, NumberFormatInfo format)
        => $"{Format(c.Left, decimals, thousands, format)} {Symbol(c.Op)} {Format(c.Right, decimals, thousands, format)} = {Format(c.Result, decimals, thousands, format)}";

    /// <summary>Adds a calculation to the front of the newline-separated tape, keeping at most <paramref name="keep"/>.</summary>
    public static string PushHistory(string tape, Calculation c, int keep)
        => string.Join("\n", new[] { EncodeCalculation(c) }.Concat(tape.Split('\n').Where(l => DecodeCalculation(l.Trim()) is not null).Select(l => l.Trim())).Take(Math.Max(0, keep)));

    public static List<Calculation> ReadHistory(string tape)
        => tape.Split('\n').Select(l => DecodeCalculation(l.Trim())).OfType<Calculation>().ToList();

    /// <summary>Saves a state as one invariant line.</summary>
    public static string Encode(State s) => string.Join(";",
        s.Display, s.Typing ? "1" : "0", s.Entered ? "1" : "0", Raw(s.Accumulator), s.Op, s.LastOp, Raw(s.LastOperand),
        s.Done.Replace(';', ' '), Raw(s.Memory), s.HasMemory ? "1" : "0", s.Error.Replace(';', ' '));

    /// <summary>Reads a saved state (Initial if missing or damaged).</summary>
    public static State Decode(string? text)
    {
        var p = (text ?? "").Split(';');
        if (p.Length != 11) return Initial;
        string display = p[0].Length > 0 && p[0].All(c => char.IsAsciiDigit(c) || c is '.' or '-') ? p[0] : "0";
        string Op(string o) => o is "+" or "-" or "*" or "/" ? o : "";
        return new State
        {
            Display = display, Typing = p[1] == "1", Entered = p[2] == "1", Accumulator = ParseValue(p[3]), Op = Op(p[4]),
            LastOp = Op(p[5]), LastOperand = ParseValue(p[6]), Done = p[7], Memory = ParseValue(p[8]), HasMemory = p[9] == "1", Error = p[10],
        };
    }
}

