//-----------------------------------------------------------------------
// <copyright file="DynamicTypeLoadingDispatcherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Util
{
    /// <summary>
    /// A <see cref="MessageDispatcherConfigurator"/> that lives outside Akka.dll, so a dispatcher
    /// <c>type</c> pointing at it is not one of the aliases <c>Dispatchers.ConfiguratorFrom</c> knows and
    /// can only be reached by name. Everything is delegated to a plain <see cref="DispatcherConfigurator"/>.
    /// </summary>
    public sealed class DelegatingTestDispatcherConfigurator : MessageDispatcherConfigurator
    {
        private readonly MessageDispatcher _inner;

        public DelegatingTestDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites)
            : base(config, prerequisites)
        {
            _inner = new DispatcherConfigurator(config, prerequisites).Dispatcher();
        }

        public override MessageDispatcher Dispatcher() => _inner;
    }

    /// <summary>
    /// An <see cref="ExecutorServiceConfigurator"/> that lives outside Akka.dll, so a dispatcher
    /// <c>executor</c> pointing at it is not one of the aliases
    /// <c>MessageDispatcherConfigurator.ConfigureExecutor</c> knows and can only be reached by name.
    /// </summary>
    public sealed class DelegatingTestExecutorConfigurator : ExecutorServiceConfigurator
    {
        private static int _constructed;

        public DelegatingTestExecutorConfigurator(Config config, IDispatcherPrerequisites prerequisites)
            : base(config, prerequisites)
        {
            Interlocked.Increment(ref _constructed);
        }

        /// <summary>
        /// How many of these have been built so far - which is how the specs tell an executor that came
        /// through this configurator from one that did not. The dispatcher it hangs off keeps the built-in
        /// <c>Dispatcher</c> type, so nothing else in the lookup would reveal it.
        /// </summary>
        public static int Constructed => Volatile.Read(ref _constructed);

        public override ExecutorService Produce(string id) => new InlineExecutorService(id);

        private sealed class InlineExecutorService : ExecutorService
        {
            public InlineExecutorService(string id) : base(id)
            {
            }

            public override void Execute(IRunnable run) => run.Run();

            public override void Shutdown()
            {
            }
        }
    }

    /// <summary>
    /// The dispatcher <c>type</c> and <c>executor</c> fallbacks that <c>Akka.DynamicTypeLoading</c> gates.
    /// Both sites keep their existing alias switch, so everything Akka.NET ships still resolves with the
    /// switch off and only a configurator named by type needs reflection.
    /// </summary>
    /// <remarks>
    /// This spec builds its own <see cref="ActorSystem"/> instead of deriving from <c>AkkaSpec</c>: Akka.TestKit
    /// configures <c>akka.test.test-actor.dispatcher</c> with
    /// <c>type = "Akka.TestKit.CallingThreadDispatcherConfigurator, Akka.TestKit"</c>, a type name that only
    /// resolves through reflection, so no TestKit-derived spec can run with the switch off.
    /// </remarks>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class DynamicTypeLoadingDispatcherSpec
    {
        private const string SwitchName = DynamicTypeLoadingCollection.Name;

        private const string CustomDispatcherTypeName = "Akka.Tests.Util.DelegatingTestDispatcherConfigurator";

        private const string CustomExecutorTypeName = "Akka.Tests.Util.DelegatingTestExecutorConfigurator";

        private const string CustomDispatcherId = "custom-type-dispatcher";

        private const string CustomExecutorDispatcherId = "custom-executor-dispatcher";

        private static Task WithDynamicTypeLoading(bool enabled, Func<Task> body)
            => AkkaFeaturesSpec.WithDynamicTypeLoading(enabled, body);

        /// <summary>
        /// One dispatcher named by <c>type</c> and one that keeps the built-in <c>Dispatcher</c> type but
        /// names its <c>executor</c>. Neither is looked up while the system boots, so both stay lazy until a
        /// spec asks for them.
        /// </summary>
        private static Config CustomDispatcherConfig()
            => ConfigurationFactory.ParseString($@"
                {CustomDispatcherId} {{
                  type = ""{CustomDispatcherTypeName}, Akka.Tests""
                }}
                {CustomExecutorDispatcherId} {{
                  type = Dispatcher
                  executor = ""{CustomExecutorTypeName}, Akka.Tests""
                }}");

        [Fact(DisplayName = "Dispatchers should still resolve the built-in dispatcher types and executors when dynamic type loading is off")]
        public async Task Should_resolve_the_built_in_dispatchers_When_dynamic_type_loading_is_disabled()
        {
            // guards the new guard being placed above the alias switch rather than inside the default arm:
            // every one of these ids reaches a case label, so none of them may ever see the switch
            var config = ConfigurationFactory.ParseString(@"
                task-dispatcher { type = TaskDispatcher }
                pinned-dispatcher { type = PinnedDispatcher }
                fork-join-dispatcher { type = ForkJoinDispatcher }
                channel-dispatcher { type = Dispatcher, executor = channel-executor }
                fork-join-executor-dispatcher { type = Dispatcher, executor = fork-join-executor }
                task-executor-dispatcher { type = Dispatcher, executor = task-executor }");

            await WithDynamicTypeLoading(false, async () =>
            {
                var system = ActorSystem.Create("built-in-dispatchers-off", config);
                try
                {
                    foreach (var id in new[]
                             {
                                 "akka.actor.default-dispatcher", "task-dispatcher", "pinned-dispatcher",
                                 "fork-join-dispatcher", "channel-dispatcher", "fork-join-executor-dispatcher",
                                 "task-executor-dispatcher"
                             })
                    {
                        system.Dispatchers.Lookup(id).Should().NotBeNull($"[{id}] is built in");
                    }
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        [Fact(DisplayName = "Dispatchers should resolve a dispatcher type and an executor that are not built in when dynamic type loading is on")]
        public async Task Should_resolve_a_custom_dispatcher_type_and_executor_When_dynamic_type_loading_is_enabled()
        {
            await WithDynamicTypeLoading(true, async () =>
            {
                var executorsBefore = DelegatingTestExecutorConfigurator.Constructed;
                var system = ActorSystem.Create("custom-dispatch-on", CustomDispatcherConfig());
                try
                {
                    // no alias arm matches these ids, so resolving at all means the reflection path ran
                    system.Dispatchers.Lookup(CustomDispatcherId).Should().NotBeNull();

                    system.Dispatchers.Lookup(CustomExecutorDispatcherId).Should().NotBeNull();
                    DelegatingTestExecutorConfigurator.Constructed.Should().Be(executorsBefore + 1);
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        [Fact(DisplayName = "Dispatchers should reject a dispatcher type and an executor that are not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_dispatcher_type_or_executor_is_not_built_in_and_dynamic_type_loading_is_disabled()
        {
            await WithDynamicTypeLoading(false, async () =>
            {
                var system = ActorSystem.Create("custom-dispatch-off", CustomDispatcherConfig());
                try
                {
                    var byType = Assert.Throws<ConfigurationException>(
                        () => system.Dispatchers.Lookup(CustomDispatcherId));

                    byType.Message.Should().Contain($"[{CustomDispatcherId}.type]");
                    byType.Message.Should().Contain(CustomDispatcherTypeName);
                    byType.Message.Should().Contain(SwitchName);

                    var byExecutor = Assert.Throws<ConfigurationException>(
                        () => system.Dispatchers.Lookup(CustomExecutorDispatcherId));

                    byExecutor.Message.Should().Contain($"[{CustomExecutorDispatcherId}.executor]");
                    byExecutor.Message.Should().Contain(CustomExecutorTypeName);
                    byExecutor.Message.Should().Contain(SwitchName);
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }
    }
}
