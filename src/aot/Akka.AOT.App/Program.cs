//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Actor.Setup;
using Akka.AOT.App.Actors;
using Akka.Dispatch;
using Akka.Event;

namespace Akka.AOT.App;

internal static class Program
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TerminateTimeout = TimeSpan.FromSeconds(30);

    private static async Task<int> Main(string[] args)
    {
        // a crash on a pool thread would otherwise kill the process with no diagnosis at all
        AppDomain.CurrentDomain.UnhandledException += static (_, e) =>
        {
            PrintFailure(e.ExceptionObject as Exception);
            Console.Out.Flush();
        };

        try
        {
            // Run 1: the bare default - no HOCON, no BootstrapSetup.
            await RunAsync("aot", static setup => ActorSystem.Create("aot", ActorSystemSetup.Create(setup)));

            // Run 2: same thing, but through an (empty) BootstrapSetup, which is the path
            // Akka.Hosting and most DI integrations take.
            await RunAsync("aot-setup", static setup =>
                ActorSystem.Create("aot-setup", ActorSystemSetup.Create(BootstrapSetup.Create(), setup)));

            Console.WriteLine("[canary] OK");
            return 0;
        }
        catch (Exception ex)
        {
            PrintFailure(ex);
            return 1;
        }
    }

    private static async Task RunAsync(string label, Func<LogFilterSetup, ActorSystem> factory)
    {
        Console.WriteLine($"[canary] creating ActorSystem '{label}' ...");

        // Serialization and Mailboxes log-and-continue when a configured type name does not resolve, so a
        // silent boot is not proof of anything. This watchdog turns any WARNING or ERROR that reaches the
        // stdout logger into a failed run.
        var watchdog = new LogWatchdogFilter();
        var system = factory(new LogFilterSetup([watchdog]));
        try
        {
            watchdog.ThrowIfAnyProblems(label, "startup");
            system.Log.Info("[canary] {0}: actor system up", label);

            var untyped = system.ActorOf(Props.Create(() => new AotUntypedActor()), "untyped-actor");
            var receive = system.ActorOf(Props.Create(() => new AotReceiveActor()), "receive-actor");

            var untypedReply = await untyped.Ask<string>($"hello untyped from {label}", AskTimeout);
            Console.WriteLine($"[canary] {label}: untyped replied '{untypedReply}'");

            var receiveReply = await receive.Ask<string>($"hello receive from {label}", AskTimeout);
            Console.WriteLine($"[canary] {label}: receive replied '{receiveReply}'");

            AssertBuiltInsResolved(label, system);

            system.Log.Info("[canary] {0}: round-trip complete", label);
            watchdog.ThrowIfAnyProblems(label, "post-boot");
        }
        finally
        {
            await system.Terminate().WaitAsync(TerminateTimeout);
        }

        Console.WriteLine($"[canary] {label}: terminated");
    }

    /// <summary>
    /// Positive assertions that the pieces core resolves from HOCON really did get built. Without these, a
    /// run could "pass" with no serializers and a default mailbox that was never registered.
    /// </summary>
    private static void AssertBuiltInsResolved(string label, ActorSystem system)
    {
        Require(label, system.Serialization.FindSerializerFor(new byte[] { 1 }) is not null,
            "no serializer for a byte[] - akka.actor.serializers/serialization-bindings did not register");

        // ByteArraySerializer needs no reflection, so 'bytes' and the System.Byte[] binding survive with the
        // switch off. Newtonsoft.Json does not, so core registers neither the 'json' alias nor the
        // System.Object binding that points at it, and a type with no binding of its own has no fallback. That
        // is the designed behavior, not a gap - so assert the throw, and assert the message tells the user what
        // to do about it.
        var unbound = RequireThrows(label, () => system.Serialization.FindSerializerFor("hello"));
        Require(label, unbound.Message.Contains("Akka.DynamicTypeLoading", StringComparison.Ordinal)
                       && unbound.Message.Contains("SerializationSetup", StringComparison.Ordinal),
            $"serializing an unbound type threw, but without saying why: [{unbound.Message}]");

        Require(label, system.Scheduler is HashedWheelTimerScheduler,
            $"akka.scheduler.implementation resolved to [{system.Scheduler.GetType().FullName}]");
        Require(label, system.Settings.LogFormatter is SemanticLogMessageFormatter,
            $"akka.logger-formatter resolved to [{system.Settings.LogFormatter.GetType().FullName}]");

        var defaultMailbox = system.Mailboxes.Lookup("akka.actor.default-mailbox");
        Require(label, defaultMailbox is UnboundedMailbox,
            $"akka.actor.default-mailbox resolved to [{defaultMailbox.GetType().FullName}]");

        Console.WriteLine($"[canary] {label}: byte[] serializer, scheduler, log formatter and default mailbox all resolved, unbound types throw as designed");
    }

    private static void Require(string label, bool condition, string problem)
    {
        if (!condition)
            throw new InvalidOperationException($"{label}: {problem}");
    }

    /// <summary>
    /// Asserts that <paramref name="action"/> fails with a <see cref="System.Runtime.Serialization.SerializationException"/> and hands the
    /// exception back so the caller can assert on its message.
    /// </summary>
    private static System.Runtime.Serialization.SerializationException RequireThrows(string label, Action action)
    {
        try
        {
            action();
        }
        catch (System.Runtime.Serialization.SerializationException ex)
        {
            return ex;
        }

        throw new InvalidOperationException(
            $"{label}: serializing a type with no serialization-binding was expected to throw with dynamic type loading off, but it succeeded");
    }

    private static void PrintFailure(Exception? ex)
    {
        if (ex is null)
        {
            Console.WriteLine("[canary] FAILED: unhandled non-exception throw");
            return;
        }

        Console.WriteLine($"[canary] FAILED: {ex.GetType().FullName}: {ex.Message}");
        Console.WriteLine(ex.StackTrace);

        var inner = ex.InnerException;
        var depth = 0;
        while (inner is not null && depth++ < 10)
        {
            Console.WriteLine($"[canary]  --> inner: {inner.GetType().FullName}: {inner.Message}");
            Console.WriteLine(inner.StackTrace);
            inner = inner.InnerException;
        }
    }
}
