using System.Reflection;

namespace PixlReplay.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute;

public static class Assert
{
    public static void Equal<T>(T expected, T actual, string? what = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what ?? "value"}: expected <{expected}> but was <{actual}>");
    }

    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static void Near(double expected, double actual, double tolerance, string? what = null)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new Exception($"{what ?? "value"}: expected {expected}±{tolerance} but was {actual}");
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        string? filter = args.Length > 0 ? args[0] : null;
        var tests = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
            .Where(m => filter == null || $"{m.DeclaringType!.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name)
            .ToList();

        int failed = 0;
        foreach (var test in tests)
        {
            string name = $"{test.DeclaringType!.Name}.{test.Name}";
            try
            {
                test.Invoke(null, null);
                Console.WriteLine($"PASS {name}");
            }
            catch (TargetInvocationException ex)
            {
                failed++;
                Console.WriteLine($"FAIL {name}: {ex.InnerException?.Message}");
                if (Environment.GetEnvironmentVariable("PIXLREPLAY_TEST_TRACE") == "1")
                    Console.WriteLine(ex.InnerException);
            }
        }

        Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
        return failed;
    }
}
