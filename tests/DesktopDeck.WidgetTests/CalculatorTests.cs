using System.Globalization;
using DesktopDeck.Widgets;

public static class CalculatorTests
{
    private static CalculatorLogic.State Keys(params string[] keys) => CalculatorLogic.Run(CalculatorLogic.Initial, keys);

    public static void ArithmeticExecutesImmediatelyAndRepeatedEqualsWorks()
    {
        var state = Keys("1", "+", "2", "*", "3", "=");
        Expect.Equal(9m, state.Value, "immediate execution");
        state = CalculatorLogic.Press(state, "=").State;
        Expect.Equal(27m, state.Value, "repeat multiplication");
    }

    public static void AllFourOperatorsAndDecimalsAreExact()
    {
        Expect.Equal(8m, Keys("1", "5", "-", "7", "=").Value, "subtract");
        Expect.Equal(2.5m, Keys("5", "/", "2", "=").Value, "divide");
        Expect.Equal(0.3m, Keys("0", ".", "1", "+", "0", ".", "2", "=").Value, "decimal sum has no float error");
        Expect.Equal(10m, Keys("5", "+", "=").Value, "5 + = doubles");
    }

    public static void PercentUsesAccumulatorOnlyForAddition()
    {
        Expect.Equal(230m, Keys("2", "0", "0", "+", "1", "5", "%", "=").Value, "add percent");
        Expect.Equal(170m, Keys("2", "0", "0", "-", "1", "5", "%", "=").Value, "subtract percent");
        Expect.Equal(30m, Keys("2", "0", "0", "*", "1", "5", "%", "=").Value, "multiply percent");
        Expect.Equal(0.5m, Keys("5", "0", "%").Value, "plain percent");
    }

    public static void SignChangeWorksWhileTypingAndOnResults()
    {
        Expect.Equal("-5", Keys("5", "neg").Display, "negate typed");
        Expect.Equal("5", Keys("5", "neg", "neg").Display, "negate twice");
        Expect.Equal(-9m, Keys("4", "+", "5", "=", "neg").Value, "negate result");
    }

    public static void BackspaceAndClearEntry()
    {
        Expect.Equal("12", Keys("1", "2", "3", "back").Display, "backspace");
        Expect.Equal("0", Keys("7", "back").Display, "backspace to zero");
        Expect.Equal(5m, Keys("5", "+", "9", "CE", "=").Value, "CE then equals reuses the displayed zero");
        Expect.Equal(0m, Keys("5", "+", "C").Value, "C clears everything");
    }

    public static void MemoryCanBeRecalledAndCleared()
    {
        var state = Keys("9", "M+", "C", "2", "M-", "MR");
        Expect.Equal(7m, state.Value, "memory subtraction");
        Expect.True(state.HasMemory, "memory marker");
        Expect.True(!CalculatorLogic.Press(state, "MC").State.HasMemory, "clear memory");
        Expect.True(Keys("5", "M+", "C").HasMemory, "clear keeps memory");
    }

    public static void DivisionErrorRecoversWithNewInput()
    {
        var state = Keys("1", "/", "0", "=");
        Expect.True(state.Error.Length > 0, "divide by zero");
        Expect.Equal(4m, CalculatorLogic.Press(state, "4").State.Value, "input recovers");
        Expect.True(Keys("9", "M+", "1", "/", "0", "=", "C").HasMemory, "memory survives an error");
        Expect.True(CalculatorLogic.Press(state, "+").State.Error.Length > 0, "operators are ignored after an error");
    }

    public static void OverflowIsReportedNotThrown()
    {
        string[] sixteenNines = Enumerable.Repeat("9", 16).ToArray();
        var state = CalculatorLogic.Run(CalculatorLogic.Initial, sixteenNines);
        state = CalculatorLogic.Run(state, ["*"]);
        state = CalculatorLogic.Run(state, sixteenNines);
        state = CalculatorLogic.Run(state, ["*"]);
        Expect.Equal("Overflow", state.Error, "overflow is an error message, never an exception");
    }

    public static void StorageAndHistoryRoundTrip()
    {
        var state = Keys("1", "2", "+", "3", "=");
        Expect.Equal(state, CalculatorLogic.Decode(CalculatorLogic.Encode(state)), "state round trip");
        Expect.Equal(CalculatorLogic.Initial, CalculatorLogic.Decode("garbage"), "damaged state resets");
        string tape = "";
        for (int i = 0; i < 20; i++) tape = CalculatorLogic.PushHistory(tape, new CalculatorLogic.Calculation(12, "+", 3, 15), 5);
        Expect.Equal(5, CalculatorLogic.ReadHistory(tape).Count, "history bound");
        Expect.Equal("12,345.60", CalculatorLogic.FormatEntry("12345.60", true, CultureInfo.InvariantCulture.NumberFormat), "typed decimals preserved");
    }

    public static void TapeEntriesAreStoredNewestFirstAndRecallable()
    {
        var (state, done) = CalculatorLogic.Press(Keys("6", "*", "7"), "=");
        Expect.True(done is not null && done.Result == 42m, "equals reports the calculation");
        string tape = CalculatorLogic.PushHistory("", done!, 10);
        tape = CalculatorLogic.PushHistory(tape, new CalculatorLogic.Calculation(1, "+", 1, 2), 10);
        Expect.Equal(2m, CalculatorLogic.ReadHistory(tape)[0].Result, "newest first");
        var recalled = CalculatorLogic.Recall(state, 42m);
        Expect.Equal(100m, CalculatorLogic.Run(recalled, "+", "5", "8", "=").Value, "recalled result starts a new calculation");
        Expect.Equal("6 × 7 = 42", CalculatorLogic.FormatCalculation(done!, -1, true, CultureInfo.InvariantCulture.NumberFormat), "tape text");
    }

    public static void EntryIsBoundedAndOperatorReplacementWorks()
    {
        var state = CalculatorLogic.Initial;
        for (int i = 0; i < 40; i++) state = CalculatorLogic.Press(state, "9").State;
        Expect.Equal(CalculatorLogic.MaxDigits, state.Display.Length, "digit bound");
        Expect.Equal(6m, Keys("8", "+", "-", "2", "=").Value, "replace pending operator");
    }

    public static void FormattingHonorsDecimalsAndGrouping()
    {
        var format = CultureInfo.InvariantCulture.NumberFormat;
        Expect.Equal("1,234,567.89", CalculatorLogic.Format(1234567.891m, 2, true, format), "fixed places with grouping");
        Expect.Equal("1234567.89", CalculatorLogic.Format(1234567.891m, 2, false, format), "no grouping");
        Expect.Equal("3.14", CalculatorLogic.Format(3.14m, -1, true, format), "automatic trims zeros");
        Expect.Equal("0.00", CalculatorLogic.Format(-0.0001m, 2, true, format), "negative zero is plain zero");
        Expect.True(CalculatorLogic.Format(1e20m, -1, true, format).Contains('E'), "huge numbers use scientific notation");
        Expect.Equal("1234.5", CalculatorLogic.PlainResult(Keys("1", "2", "3", "4", ".", "5", "="), -1, format), "copy text has no separators");
    }
}
