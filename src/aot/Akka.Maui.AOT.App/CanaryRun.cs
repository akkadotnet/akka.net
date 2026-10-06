//-----------------------------------------------------------------------
// <copyright file="CanaryRun.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Maui.AOT.App;

/// <summary>
/// Process-wide state for one run: whether this is a CI self-test, the log watchdog, and the exits.
/// </summary>
internal static class CanaryRun
{
    public const string Prefix = "[canary-maui]";

    /// <summary>The whole self-test, UI start-up included, must finish inside this.</summary>
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromMinutes(3);

    private static int _initialized;
    private static int _exiting;

    public static CanaryLoggerProvider Logs { get; } = new();

    /// <summary>
    /// On when the AKKA_CANARY_SELFTEST environment variable is 1 or the app gets a --selftest argument. On Android,
    /// MainActivity also turns it on for a 'selftest' intent extra.
    /// </summary>
    public static bool SelfTest { get; private set; }

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return;

        if (Environment.GetEnvironmentVariable("AKKA_CANARY_SELFTEST") == "1"
            || Environment.GetCommandLineArgs().Contains("--selftest"))
        {
            EnableSelfTest();
        }
    }

    public static void EnableSelfTest()
    {
        if (SelfTest)
            return;

        SelfTest = true;
        Print($"self-test mode (overall timeout {OverallTimeout.TotalSeconds:F0}s)");

        // a crash on a pool thread would otherwise end the process with no diagnosis at all
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fail("unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Fail("unobserved task exception", e.Exception);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Print("ProcessExit raised");

        _ = Task.Run(async () =>
        {
            await Task.Delay(OverallTimeout);
            Fail($"the self-test did not finish within {OverallTimeout.TotalSeconds:F0}s", null, exitCode: 2);
        });
    }

    public static void Print(string line)
    {
        Console.WriteLine($"{Prefix} {line}");
        Console.Out.Flush();
    }

    /// <summary>Prints the failure and ends the process with a non-zero exit code, unless an exit is already under way.</summary>
    public static void Fail(string what, Exception? ex, int exitCode = 1)
    {
        if (Interlocked.Exchange(ref _exiting, 1) == 1)
            return;

        Print($"FAILED: {what}");
        if (ex is not null)
            Console.WriteLine(ex.ToString());
        Console.Out.Flush();
        Environment.Exit(exitCode);
    }

    /// <summary>
    /// Ends a passing run the way a user would: MAUI's own Application.Quit(). If that does not end the process
    /// (it is not implemented on every platform) the run still passes, and says so.
    /// </summary>
    public static void QuitAfterSuccess()
    {
        if (Interlocked.Exchange(ref _exiting, 1) == 1)
            return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            Print("Application.Quit() did not end the process within 10s; exiting with Environment.Exit(0)");
            Environment.Exit(0);
        });

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                Print("calling Application.Quit()");
                Application.Current?.Quit();
            }
            catch (Exception ex)
            {
                Print($"Application.Quit() threw {ex.GetType().Name}: {ex.Message}; exiting with Environment.Exit(0)");
                Environment.Exit(0);
            }
        });
    }
}
