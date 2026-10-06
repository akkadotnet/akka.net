//-----------------------------------------------------------------------
// <copyright file="LoggerSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Xunit;
using Akka.Tests.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Loggers;

/// <summary>
/// A logger actor generic over a marker type, so each spec's own marker (e.g. <c>LoggerA</c> vs
/// <c>LoggerB</c>) makes a distinct closed generic type with its own instance counter and forwarding
/// probe - lets a spec name the "same" logger through both <c>akka.loggers</c> and a
/// <see cref="LoggerSetup"/> and check it started once.
/// </summary>
public sealed class CapturingLogger<TMarker> : ActorBase, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
{
    private static int _instances;
    public static int Instances => _instances;
    public static IActorRef? Probe { get; set; }

    public CapturingLogger() => Interlocked.Increment(ref _instances);

    protected override bool Receive(object message)
    {
        switch (message)
        {
            case InitializeLogger:
                Sender.Tell(new LoggerInitialized());
                return true;
            case LogEvent logEvent:
                Probe?.Tell(logEvent);
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// A minimal <see cref="ILogMessageFormatter"/> stand-in for a third-party formatter (e.g.
/// Akka.Logger.Serilog's <c>SerilogLogMessageFormatter</c>) - wraps the formatted text so a spec can
/// tell it apart from <see cref="DefaultLogMessageFormatter"/>'s output.
/// </summary>
public sealed class MarkerLogMessageFormatter : ILogMessageFormatter
{
    public string Format(string format, params object[] args) => $"<<{string.Format(format, args)}>>";

    public string Format(string format, IEnumerable<object> args) => Format(format, args.ToArray());
}

public class AdditiveLoggerSetupSpec : AkkaSpec
{
    private sealed class LoggerA { }
    private sealed class LoggerB { }

    private static readonly string LoggerATypeName =
        $"{typeof(CapturingLogger<LoggerA>).FullName}, {typeof(CapturingLogger<LoggerA>).Assembly.GetName().Name}";

    private static ActorSystemSetup CreateSetup()
    {
        var config = ConfigurationFactory.ParseString($@"akka.loggers = [""{LoggerATypeName}""]");
        var loggerSetup = LoggerSetup.Create(
            Props.Create<CapturingLogger<LoggerB>>(),
            Props.Create<CapturingLogger<LoggerA>>());
        return ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), loggerSetup);
    }

    public AdditiveLoggerSetupSpec(ITestOutputHelper output)
        : base(CreateSetup(), output: output)
    {
    }

    [Fact(DisplayName = "Should_StartLoggerOnce_When_TheSameTypeIsInHoconAndLoggerSetup")]
    public async Task Should_StartLoggerOnce_When_TheSameTypeIsInHoconAndLoggerSetup()
    {
        var probeB = CreateTestProbe();
        CapturingLogger<LoggerB>.Probe = probeB;

        Logging.GetLogger(Sys, "AdditiveSource").Warning("Hello");

        // B only exists via the LoggerSetup - proves the setup is additive to akka.loggers, not a
        // replacement for it.
        await probeB.ExpectMsgAsync<Warning>(TimeSpan.FromSeconds(5));

        // A is named by both akka.loggers and the LoggerSetup - only the LoggerSetup instance starts.
        CapturingLogger<LoggerA>.Instances.Should().Be(1);
    }
}

[Collection(DynamicTypeLoadingCollection.Name)]
public class DynamicTypeLoadingOffLoggerSetupSpec
{
    private sealed class OffMarker { }

    [Fact(DisplayName = "Should_StartLoggerAndUseItsFormatter_When_DynamicTypeLoadingIsDisabled")]
    public async Task Should_StartLoggerAndUseItsFormatter_When_DynamicTypeLoadingIsDisabled()
    {
        var formatter = new MarkerLogMessageFormatter();
        var loggerSetup = LoggerSetup.Create(formatter, Props.Create<CapturingLogger<OffMarker>>());
        ActorSystem? sys = null;

        // Only the system start needs to run with the switch off - a type-based LoggerSetup
        // registration and its formatter need no Type.GetType, so both start fine without dynamic
        // type loading. The TestKit's own probe plumbing resolves its dispatcher by reflection and
        // isn't part of what this spec is checking, so the switch is back on before that runs.
        await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
        {
            sys = ActorSystem.Create("DynamicTypeLoadingOffLoggerSetupSpec", ActorSystemSetup.Create(loggerSetup));
            return Task.CompletedTask;
        });

        try
        {
            sys!.Settings.LogFormatter.Should().BeSameAs(formatter);

            var probe = new TestProbe(sys, new XunitAssertions());
            CapturingLogger<OffMarker>.Probe = probe;

            Logging.GetLogger(sys, "OffSource").Warning("NoReflectionNeeded {0}", "here");

            var warning = await probe.ExpectMsgAsync<Warning>(TimeSpan.FromSeconds(5));
            warning.Message.ToString().Should().Be("<<NoReflectionNeeded here>>");
        }
        finally
        {
            if (sys != null)
                await sys.Terminate();
        }
    }
}

/// <summary>
/// Not generic on purpose: a generic type's name carries its arguments' assembly identities, which no
/// HOCON author writes by hand.
/// Constructions are counted per <see cref="ActorSystem"/> rather than process-wide: a spec that expects
/// <c>ActorSystem.Create</c> to throw leaves its logger to be constructed in the background, and that
/// late construction must not be counted against another spec's system (see #8772).
/// </summary>
public sealed class NamedInHoconLogger : ActorBase, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
{
    private sealed class Counter
    {
        public int Value;
    }

    private static readonly ConditionalWeakTable<ActorSystem, Counter> Counts = new();

    public static int InstancesIn(ActorSystem system) =>
        Counts.TryGetValue(system, out var counter) ? Volatile.Read(ref counter.Value) : 0;

    public NamedInHoconLogger() => Interlocked.Increment(ref Counts.GetOrCreateValue(Context.System).Value);

    protected override bool Receive(object message)
    {
        if (message is not InitializeLogger)
            return false;

        Sender.Tell(new LoggerInitialized());
        return true;
    }
}

[Collection(DynamicTypeLoadingCollection.Name)]
public class DynamicTypeLoadingOffLoggerSetupNamedInHoconSpec
{
    private static ActorSystemSetup SetupFor(string hoconTypeName)
    {
        var config = ConfigurationFactory.ParseString($@"akka.loggers = [""{hoconTypeName}""]");
        return ActorSystemSetup.Create(
            BootstrapSetup.Create().WithConfig(config),
            LoggerSetup.Create(Props.Create<NamedInHoconLogger>()));
    }

    [Theory(DisplayName = "Should_StartLoggerOnce_When_HoconNamesATypeLoggerSetupRegisters_AndDynamicTypeLoadingIsDisabled")]
    [InlineData("Akka.Tests.Loggers.NamedInHoconLogger, Akka.Tests")]
    [InlineData("Akka.Tests.Loggers.NamedInHoconLogger,akka.tests")]
    [InlineData("Akka.Tests.Loggers.NamedInHoconLogger, Akka.Tests, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null")]
    public async Task Should_StartLoggerOnce_When_HoconNamesATypeLoggerSetupRegisters_AndDynamicTypeLoadingIsDisabled(
        string hoconTypeName)
    {
        ActorSystem? sys = null;

        await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
        {
            sys = ActorSystem.Create("NamedInHocon", SetupFor(hoconTypeName));
            return Task.CompletedTask;
        });

        try
        {
            NamedInHoconLogger.InstancesIn(sys!).Should().Be(1);
        }
        finally
        {
            if (sys != null)
                await sys.Terminate();
        }
    }

    [Theory(DisplayName = "Should_Throw_When_HoconNamesATypeLoggerSetupDoesNotRegister_AndDynamicTypeLoadingIsDisabled")]
    [InlineData("Akka.Tests.Loggers.NoSuchLogger, Akka.Tests")]
    [InlineData("Akka.Tests.Loggers.NamedInHoconLogger, Akka.Remote")]
    [InlineData("Akka.Tests.Loggers.NamedInHoconLogger")]
    public async Task Should_Throw_When_HoconNamesATypeLoggerSetupDoesNotRegister_AndDynamicTypeLoadingIsDisabled(
        string hoconTypeName)
    {
        await AkkaFeaturesSpec.WithDynamicTypeLoading(false, () =>
        {
            Action create = () => ActorSystem.Create("NamedInHoconRejected", SetupFor(hoconTypeName));
            create.Should().Throw<ConfigurationException>().WithMessage("*not built in*");
            return Task.CompletedTask;
        });
    }
}

public class LoggerSetupFormatterPrecedenceSpec : AkkaSpec
{
    private static readonly MarkerLogMessageFormatter SetupFormatter = new();

    private static ActorSystemSetup CreateSetup()
    {
        // A built-in, resolvable value, so a reversed precedence would still fail the assertion below
        // with a *different* formatter instance rather than an exception.
        var config = ConfigurationFactory.ParseString(
            @"akka.logger-formatter = ""Akka.Event.SemanticLogMessageFormatter""");
        return ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), LoggerSetup.Create(SetupFormatter));
    }

    public LoggerSetupFormatterPrecedenceSpec(ITestOutputHelper output)
        : base(CreateSetup(), output: output)
    {
    }

    [Fact(DisplayName = "Should_PreferLoggerSetupFormatter_When_AkkaLoggerFormatterIsAlsoSet")]
    public void Should_PreferLoggerSetupFormatter_When_AkkaLoggerFormatterIsAlsoSet()
    {
        Sys.Settings.LogFormatter.Should().BeSameAs(SetupFormatter);
    }
}
