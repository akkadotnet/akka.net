//-----------------------------------------------------------------------
// <copyright file="DynamicTypeLoadingOffSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using FluentAssertions;
using Xunit;
using XunitTestKit = Akka.TestKit.Xunit.TestKit;

namespace Akka.TestKit.Tests
{
    /// <summary>
    /// <see cref="AppContext"/> switches are process-wide, so anything that flips
    /// <c>Akka.DynamicTypeLoading</c> belongs in this collection and never runs beside another spec.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class DynamicTypeLoadingCollection
    {
        public const string Name = "Akka.DynamicTypeLoading";
    }

    /// <summary>
    /// Akka.TestKit names <c>TestEventListener</c> and <c>CallingThreadDispatcherConfigurator</c> by type in
    /// its default HOCON, which only resolves through reflection. These specs start a real TestKit with the
    /// <c>Akka.DynamicTypeLoading</c> switch off and use both.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class DynamicTypeLoadingOffSpec
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        private const string TestActorDispatcherId = "akka.test.test-actor.dispatcher";

        private static async Task WithDynamicTypeLoadingOff(Func<Task> body)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, false);
            try
            {
                await body();
            }
            finally
            {
                AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
            }
        }

        private sealed class EchoActor : ReceiveActor
        {
            public EchoActor()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        /// <summary>
        /// A logger that is not part of Akka.TestKit, to show a <see cref="LoggerSetup"/> the caller passed in
        /// still starts next to the one TestKit adds.
        /// </summary>
        private sealed class RecordingLogger : ReceiveActor
        {
            public static readonly ConcurrentQueue<string> Messages = new();

            public RecordingLogger()
            {
                Receive<InitializeLogger>(init =>
                {
                    init.LoggingBus.Subscribe(Self, typeof(Info));
                    Sender.Tell(new LoggerInitialized());
                });
                Receive<Info>(info => Messages.Enqueue(info.Message.ToString() ?? string.Empty));
            }
        }

        [Fact(DisplayName = "TestKit should start and deliver to the test actor when dynamic type loading is off")]
        public async Task Should_start_TestKit_When_dynamic_type_loading_is_off()
        {
            await WithDynamicTypeLoadingOff(async () =>
            {
                await using var kit = new XunitTestKit();

                kit.TestActor.Tell("hello");
                await kit.ExpectMsgAsync("hello", TimeSpan.FromSeconds(3));
            });
        }

        [Fact(DisplayName = "EventFilter should intercept log events when dynamic type loading is off")]
        public async Task Should_intercept_log_events_When_dynamic_type_loading_is_off()
        {
            await WithDynamicTypeLoadingOff(async () =>
            {
                await using var kit = new XunitTestKit();

                await kit.EventFilter.Info(contains: "marker-message")
                    .ExpectOneAsync(() =>
                    {
                        kit.Sys.Log.Info("marker-message");
                        return Task.CompletedTask;
                    });

                await kit.EventFilter.DeadLetter()
                    .ExpectAsync(1, async () =>
                    {
                        kit.Sys.DeadLetters.Tell("nobody-home");
                        await Task.CompletedTask;
                    });
            });
        }

        [Fact(DisplayName = "CallingThreadDispatcher should run actors on the calling thread when dynamic type loading is off")]
        public async Task Should_run_actors_on_calling_thread_When_dynamic_type_loading_is_off()
        {
            await WithDynamicTypeLoadingOff(async () =>
            {
                await using var kit = new XunitTestKit();

                kit.Sys.Dispatchers.Lookup(CallingThreadDispatcher.Id).Should().BeOfType<CallingThreadDispatcher>();
                kit.Sys.Dispatchers.Lookup(TestActorDispatcherId).Should().BeOfType<CallingThreadDispatcher>();

                var echo = kit.Sys.ActorOf(Props.Create<EchoActor>().WithDispatcher(CallingThreadDispatcher.Id));
                echo.Tell("ping", kit.TestActor);

                // the calling thread already delivered the reply, so it is waiting by the time Tell returns
                await kit.ExpectMsgAsync("ping", TimeSpan.FromSeconds(3));

                var testActorRef = kit.ActorOfAsTestActorRef<EchoActor>(Props.Create<EchoActor>());
                testActorRef.Tell("pong", kit.TestActor);
                await kit.ExpectMsgAsync("pong", TimeSpan.FromSeconds(3));
            });
        }

        [Fact(DisplayName = "TestKit should keep a LoggerSetup the caller supplied when dynamic type loading is off")]
        public async Task Should_keep_caller_LoggerSetup_When_dynamic_type_loading_is_off()
        {
            await WithDynamicTypeLoadingOff(async () =>
            {
                var setup = ActorSystemSetup.Create(LoggerSetup.Create(Props.Create<RecordingLogger>()));
                await using var kit = new XunitTestKit(setup);

                var marker = $"recorded-{Guid.NewGuid():N}";
                await kit.EventFilter.Info(contains: marker)
                    .ExpectOneAsync(() =>
                    {
                        kit.Sys.Log.Info(marker);
                        return Task.CompletedTask;
                    });

                await kit.AwaitAssertAsync(
                    () => RecordingLogger.Messages.Any(m => m.Contains(marker)).Should().BeTrue(),
                    TimeSpan.FromSeconds(3));
            });
        }

        [Fact(DisplayName = "TestKit should leave akka.loggers alone when the config replaces it and dynamic type loading is off")]
        public async Task Should_not_add_TestEventListener_When_config_replaces_loggers()
        {
            await WithDynamicTypeLoadingOff(async () =>
            {
                var config = ConfigurationFactory.ParseString(@"
                    akka.loggers = [""Akka.Event.DefaultLogger""]
                    akka.test.filter-leeway = 200ms");
                await using var kit = new XunitTestKit(config);

                // no TestEventListener, so nothing intercepts and the filter gives up
                Func<Task> intercept = () => kit.EventFilter.Info(contains: "nobody-listens")
                    .ExpectOneAsync(() =>
                    {
                        kit.Sys.Log.Info("nobody-listens");
                        return Task.CompletedTask;
                    });
                await intercept.Should().ThrowAsync<Exception>();
            });
        }
    }
}
