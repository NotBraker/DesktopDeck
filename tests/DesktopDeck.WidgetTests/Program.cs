using System.Reflection;

// Runs every public static parameterless method of every class whose name ends in "Tests".
// Each widget adds its own *Tests.cs file; a test fails by throwing (use Expect).
int passed = 0, failed = 0;
foreach (var type in Assembly.GetExecutingAssembly().GetTypes().Where(t => t.Name.EndsWith("Tests", StringComparison.Ordinal) && t.IsClass).OrderBy(t => t.Name))
{
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.GetParameters().Length == 0 && m.ReturnType == typeof(void)).OrderBy(m => m.Name))
    {
        try { method.Invoke(null, null); passed++; Console.WriteLine($"PASS {type.Name}.{method.Name}"); }
        catch (TargetInvocationException ex) { failed++; Console.Error.WriteLine($"FAIL {type.Name}.{method.Name}: {ex.InnerException?.Message}"); }
    }
}
Console.WriteLine($"{passed}/{passed + failed} widget tests passed.");
return failed == 0 ? 0 : 1;

/// <summary>Assertions for widget tests.</summary>
public static class Expect
{
    public static void True(bool condition, string what) { if (!condition) throw new Exception(what); }
    public static void Equal<T>(T expected, T actual, string what) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected {expected}, got {actual}"); }
    public static void Near(double expected, double actual, double tolerance, string what) { if (Math.Abs(expected - actual) > tolerance) throw new Exception($"{what}: expected {expected}±{tolerance}, got {actual}"); }
    public static void Throws<TException>(Action action, string what) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new Exception($"{what}: expected {typeof(TException).Name}");
    }
}
