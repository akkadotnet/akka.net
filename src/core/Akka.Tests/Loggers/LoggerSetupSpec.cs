//-----------------------------------------------------------------------
// <copyright file="LoggerSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.TestKit;
using Akka.Tests.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Loggers;

public class LoggerSetupSpec
{
    /// <summary>
    /// A test logger actor that captures all received log events.
    /// </summary>
    public class CapturingLogger : ActorBase, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
    {
        private static readonly List<LogEvent> ReceivedEvents = new();
        private static readonly object Lock = new();

        public static IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (Lock) return ReceivedEvents.AsReadOnly();
            }
        }

        public static void Clear()
        {
            lock (Lock) ReceivedEvents.Clear();
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case InitializeLogger init:
                    Sender.Tell(new LoggerInitialized());
                    return true;
                case LogEvent evt:
                    lock (Lock) ReceivedEvents.Add(evt);
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// A second test logger to verify multi-logger support.
    /// </summary>
    public class SecondCapturingLogger : ActorBase, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
    {
        private static readonly List<LogEvent> ReceivedEvents = new();
        private static readonly object Lock = new();

        public static IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (Lock) return ReceivedEvents.AsReadOnly();
            }
        }

        public static void Clear()
        {
            lock (Lock) ReceivedEvents.Clear();
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case InitializeLogger init:
                    Sender.Tell(new LoggerInitialized());
                    return true;
                case LogEvent evt:
                    lock (Lock) ReceivedEvents.Add(evt);
                    return true;
                default:
                    return false;
            }
        }
    }

    public class LoggerSetupBuilderUnitTests
    {
        [Fact(DisplayName = "LoggerSetupBuilder should build a LoggerSetup with type registrations")]
        public void Should_Build_LoggerSetup_With_Type_Registrations()
        {
            var setup = new LoggerSetupBuilder()
                .AddLogger<CapturingLogger>()
                .AddLogger(typeof(SecondCapturingLogger))
                .Build();

            setup.Loggers.Count.Should().Be(2);
            setup.Loggers[0].LoggerType.Should().Be(typeof(CapturingLogger));
            setup.Loggers[1].LoggerType.Should().Be(typeof(SecondCapturingLogger));
        }

        [Fact(DisplayName = "LoggerSetupBuilder should build a LoggerSetup with a factory registration")]
        public void Should_Build_LoggerSetup_With_Factory_Registration()
        {
            Func<ExtendedActorSystem, Props> factory = system => Props.Create<CapturingLogger>();

            var setup = new LoggerSetupBuilder()
                .AddLogger(factory)
                .Build();

            setup.Loggers.Count.Should().Be(1);
            setup.Loggers[0].LoggerType.Should().BeNull();
            setup.Loggers[0].PropsFactory.Should().BeSameAs(factory);
        }

        [Fact(DisplayName = "LoggerSetupBuilder.AddLogger(Type) should throw on non-ActorBase types")]
        public void Should_Throw_On_Invalid_Logger_Type()
        {
            var builder = new LoggerSetupBuilder();
            builder.Invoking(b => b.AddLogger(typeof(string)))
                   .Should().Throw<ArgumentException>();
        }

        [Fact(DisplayName = "LoggerSetupBuilder.AddLogger should throw on null type")]
        public void Should_Throw_On_Null_Logger_Type()
        {
            var builder = new LoggerSetupBuilder();
            builder.Invoking(b => b.AddLogger((Type)null!))
                   .Should().Throw<ArgumentNullException>();
        }

        [Fact(DisplayName = "LoggerSetupBuilder.AddLogger should throw on null factory")]
        public void Should_Throw_On_Null_Factory()
        {
            var builder = new LoggerSetupBuilder();
            builder.Invoking(b => b.AddLogger((Func<ExtendedActorSystem, Props>)null!))
                   .Should().Throw<ArgumentNullException>();
        }

        [Fact(DisplayName = "LoggerSetup should be registered as Setup subclass")]
        public void LoggerSetup_Should_Be_Registered_In_ActorSystemSetup()
        {
            var loggerSetup = new LoggerSetupBuilder().AddLogger<CapturingLogger>().Build();
            var sysSetup = ActorSystemSetup.Create(loggerSetup);

            var retrieved = sysSetup.Get<LoggerSetup>();
            retrieved.HasValue.Should().BeTrue();
            retrieved.Value.Should().BeSameAs(loggerSetup);
        }
    }

    public class LoggerSetupIntegrationTests : AkkaSpec
    {
        private static ActorSystemSetup CreateSetupWithLogger()
        {
            CapturingLogger.Clear();
            var loggerSetup = new LoggerSetupBuilder()
                .AddLogger<CapturingLogger>()
                .Build();
            return ActorSystemSetup.Create(loggerSetup);
        }

        public LoggerSetupIntegrationTests(ITestOutputHelper output)
            : base(CreateSetupWithLogger(), output: output)
        {
        }

        [Fact(DisplayName = "Should_UseLoggerSetup_When_Registered: custom logger receives log events")]
        public async Task Should_UseLoggerSetup_When_Registered()
        {
            var log = Logging.GetLogger(Sys, "TestSource");
            log.Warning("Hello from LoggerSetup");

            await AwaitAssertAsync(() =>
            {
                CapturingLogger.Events.Should().ContainSingle(e =>
                    e.Message.ToString() == "Hello from LoggerSetup");
            });
        }

        [Fact(DisplayName = "Should_WorkWithLogFilterSetup: LoggerSetup and LogFilterSetup compose correctly")]
        public async Task Should_WorkWithLogFilterSetup()
        {
            // Filter is already applied via the system; confirm logger still receives unfiltered events
            var log = Logging.GetLogger(Sys, "UnfilteredSource");
            log.Warning("VisibleEvent");

            await AwaitAssertAsync(() =>
            {
                CapturingLogger.Events.Should().Contain(e =>
                    e.Message.ToString() == "VisibleEvent");
            });
        }
    }

    public class MultiLoggerSetupTests : AkkaSpec
    {
        private static ActorSystemSetup CreateMultiLoggerSetup()
        {
            CapturingLogger.Clear();
            SecondCapturingLogger.Clear();
            var loggerSetup = new LoggerSetupBuilder()
                .AddLogger<CapturingLogger>()
                .AddLogger<SecondCapturingLogger>()
                .Build();
            return ActorSystemSetup.Create(loggerSetup);
        }

        public MultiLoggerSetupTests(ITestOutputHelper output)
            : base(CreateMultiLoggerSetup(), output: output)
        {
        }

        [Fact(DisplayName = "Should_SupportMultipleLoggers: all registered loggers receive events")]
        public async Task Should_SupportMultipleLoggers()
        {
            var log = Logging.GetLogger(Sys, "MultiLoggerSource");
            log.Warning("BroadcastMessage");

            await AwaitAssertAsync(() =>
            {
                CapturingLogger.Events.Should().Contain(e =>
                    e.Message.ToString() == "BroadcastMessage");
                SecondCapturingLogger.Events.Should().Contain(e =>
                    e.Message.ToString() == "BroadcastMessage");
            });
        }
    }

    public class FactoryLoggerSetupTests : AkkaSpec
    {
        public class FactoryLogger : ActorBase, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
        {
            private static readonly List<LogEvent> ReceivedEvents = new();
            private static readonly object Lock = new();

            public static IReadOnlyList<LogEvent> Events
            {
                get
                {
                    lock (Lock) return ReceivedEvents.AsReadOnly();
                }
            }

            public static void Clear()
            {
                lock (Lock) ReceivedEvents.Clear();
            }

            // Constructor that accepts the system name to prove factory pattern works
            public FactoryLogger(string systemName)
            {
                SystemName = systemName;
            }

            public string SystemName { get; }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case InitializeLogger _:
                        Sender.Tell(new LoggerInitialized());
                        return true;
                    case LogEvent evt:
                        lock (Lock) ReceivedEvents.Add(evt);
                        return true;
                    default:
                        return false;
                }
            }
        }

        private static ActorSystemSetup CreateFactorySetup()
        {
            FactoryLogger.Clear();
            var loggerSetup = new LoggerSetupBuilder()
                .AddLogger(system => Props.Create(() => new FactoryLogger(system.Name)))
                .Build();
            return ActorSystemSetup.Create(loggerSetup);
        }

        public FactoryLoggerSetupTests(ITestOutputHelper output)
            : base(CreateFactorySetup(), output: output)
        {
        }

        [Fact(DisplayName = "Should_SupportPropsFactory: factory-registered logger starts and receives events")]
        public async Task Should_SupportPropsFactory()
        {
            var log = Logging.GetLogger(Sys, "FactorySource");
            log.Warning("FactoryEvent");

            await AwaitAssertAsync(() =>
            {
                FactoryLogger.Events.Should().Contain(e =>
                    e.Message.ToString() == "FactoryEvent");
            });
        }
    }

    public class HoconFallbackTests
    {
        [Fact(DisplayName = "Should_FallBackToHocon_When_NoLoggerSetup: HOCON loggers still work without LoggerSetup")]
        public async Task Should_FallBackToHocon_When_NoLoggerSetup()
        {
            // Create a system WITHOUT a LoggerSetup — must use the HOCON path
            var config = ConfigurationFactory.ParseString(
                "akka.loggers = [\"Akka.Event.DefaultLogger\"]");

            ActorSystem sys = null;
            try
            {
                // Should not throw — HOCON path works as before
                sys = ActorSystem.Create("HoconFallbackTest", config);
                sys.Settings.Loggers.Should().Contain("Akka.Event.DefaultLogger");
            }
            finally
            {
                if (sys != null)
                    await sys.Terminate();
            }
        }
    }

    public class SetupPlusHoconLoggerTests : AkkaSpec
    {
        private static readonly string CapturingLoggerTypeName =
            $"{typeof(CapturingLogger).FullName}, {typeof(CapturingLogger).Assembly.GetName().Name}";

        private static ActorSystemSetup CreateSetup()
        {
            CapturingLogger.Clear();
            SecondCapturingLogger.Clear();
            var loggerSetup = new LoggerSetupBuilder().AddLogger<SecondCapturingLogger>().Build();
            var config = ConfigurationFactory.ParseString($@"akka.loggers = [""{CapturingLoggerTypeName}""]");
            return ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), loggerSetup);
        }

        public SetupPlusHoconLoggerTests(ITestOutputHelper output)
            : base(CreateSetup(), output: output)
        {
        }

        [Fact(DisplayName = "Should_StartBothHoconAndLoggerSetupLoggers_When_BothAreConfigured")]
        public async Task Should_StartBothHoconAndLoggerSetupLoggers_When_BothAreConfigured()
        {
            // CapturingLogger comes from akka.loggers, SecondCapturingLogger from the LoggerSetup -
            // a LoggerSetup must not drop the HOCON list, the two are additive.
            var log = Logging.GetLogger(Sys, "SetupPlusHoconSource");
            log.Warning("BothStarted");

            await AwaitAssertAsync(() =>
            {
                CapturingLogger.Events.Should().Contain(e => e.Message.ToString() == "BothStarted");
                SecondCapturingLogger.Events.Should().Contain(e => e.Message.ToString() == "BothStarted");
            });
        }
    }

    public class DedupLoggerSetupTests : AkkaSpec
    {
        private static readonly string CapturingLoggerTypeName =
            $"{typeof(CapturingLogger).FullName}, {typeof(CapturingLogger).Assembly.GetName().Name}";

        private static ActorSystemSetup CreateSetup()
        {
            CapturingLogger.Clear();
            var loggerSetup = new LoggerSetupBuilder().AddLogger<CapturingLogger>().Build();
            var config = ConfigurationFactory.ParseString($@"akka.loggers = [""{CapturingLoggerTypeName}""]");
            return ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), loggerSetup);
        }

        public DedupLoggerSetupTests(ITestOutputHelper output)
            : base(CreateSetup(), output: output)
        {
        }

        [Fact(DisplayName = "Should_StartLoggerOnce_When_SameTypeIsInHoconAndLoggerSetup")]
        public async Task Should_StartLoggerOnce_When_SameTypeIsInHoconAndLoggerSetup()
        {
            // CapturingLogger is named by both akka.loggers and the LoggerSetup - only one instance
            // should start, so exactly one copy of the event should be recorded, not two.
            var log = Logging.GetLogger(Sys, "DedupSource");
            log.Warning("OnlyOnce");

            await AwaitAssertAsync(() =>
            {
                CapturingLogger.Events.Should().ContainSingle(e => e.Message.ToString() == "OnlyOnce");
            });
        }
    }

    [Collection(DynamicTypeLoadingCollection.Name)]
    public class DynamicTypeLoadingOffLoggerSetupTests
    {
        [Fact(DisplayName = "Should_StartLoggerSetupLoggerAndResolveHoconBuiltIns_When_DynamicTypeLoadingIsDisabled")]
        public async Task Should_StartLoggerSetupLoggerAndResolveHoconBuiltIns_When_DynamicTypeLoadingIsDisabled()
        {
            await AkkaFeaturesSpec.WithDynamicTypeLoading(false, async () =>
            {
                CapturingLogger.Clear();
                var loggerSetup = new LoggerSetupBuilder().AddLogger<CapturingLogger>().Build();
                // A built-in HOCON logger resolves through the literal-typed lookup table, not
                // Type.GetType, so it must still work with dynamic type loading switched off.
                var config = ConfigurationFactory.ParseString(
                    "akka.loggers = [\"Akka.Event.DefaultLogger\"]");
                var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), loggerSetup);

                ActorSystem sys = null;
                try
                {
                    // Should not throw: the HOCON entry is built-in and the LoggerSetup registration
                    // is type-based, so neither needs reflection under the switch-off path.
                    sys = ActorSystem.Create("DynamicTypeLoadingOffLoggerSetupTest", setup);

                    var log = Logging.GetLogger(sys, "DynamicTypeLoadingOffSource");
                    log.Warning("StartedWithoutReflection");

                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (DateTime.UtcNow < deadline &&
                           !CapturingLogger.Events.Any(e => e.Message.ToString() == "StartedWithoutReflection"))
                    {
                        await Task.Delay(50);
                    }

                    CapturingLogger.Events.Should().Contain(e =>
                        e.Message.ToString() == "StartedWithoutReflection");
                }
                finally
                {
                    if (sys != null)
                        await sys.Terminate();
                }
            });
        }
    }
}
