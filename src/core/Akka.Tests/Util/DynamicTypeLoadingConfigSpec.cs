//-----------------------------------------------------------------------
// <copyright file="DynamicTypeLoadingConfigSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Routing;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Util
{
    /// <summary>
    /// A <see cref="RouterConfig"/> that lives outside Akka.dll, so it is not in
    /// <c>Deployer.BuiltInRouterConfigs</c> and can only be reached by name. Nothing ever routes through
    /// it - the specs only care whether the deployment could be parsed.
    /// </summary>
    public sealed class DelegatingTestRouter : RouterConfig
    {
        public DelegatingTestRouter(Config config)
        {
            Config = config;
        }

        public Config Config { get; }

        public override Router CreateRouter(ActorSystem system) => throw new NotSupportedException();

        public override ActorBase CreateRouterActor() => throw new NotSupportedException();

        public override ISurrogate ToSurrogate(ActorSystem system) => throw new NotSupportedException();
    }

    /// <summary>
    /// The two HOCON type-name sites <c>Akka.DynamicTypeLoading</c> gates outside the actor system's own boot
    /// chain: the routers behind <c>akka.actor.router.type-mapping</c> and the
    /// <c>akka.actor.guardian-supervisor-strategy</c> configurator.
    /// </summary>
    /// <remarks>
    /// This spec builds its own <see cref="ActorSystem"/> instead of deriving from <c>AkkaSpec</c>: Akka.TestKit
    /// configures <c>akka.test.test-actor.dispatcher</c> with
    /// <c>type = "Akka.TestKit.CallingThreadDispatcherConfigurator, Akka.TestKit"</c>, a type name that only
    /// resolves through reflection, so no TestKit-derived spec can run with the switch off.
    /// </remarks>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class DynamicTypeLoadingConfigSpec
    {
        private const string SwitchName = DynamicTypeLoadingCollection.Name;

        private const string CustomRouterTypeName = "Akka.Tests.Util.DelegatingTestRouter";

        private const string CustomSupervisorStrategyTypeName = "Akka.Tests.Actor.TestStrategy";

        private static Task WithDynamicTypeLoading(bool enabled, Func<Task> body)
            => AkkaFeaturesSpec.WithDynamicTypeLoading(enabled, body);

        /// <summary>
        /// Every alias in the shipped <c>akka.actor.router.type-mapping</c> whose mapped type lives inside
        /// Akka.dll. The two <c>cluster-metrics-adaptive-*</c> aliases point at Akka.Cluster.Metrics and stay
        /// on the reflection path, so they are filtered out here.
        /// </summary>
        private static IReadOnlyList<(string Alias, Type RouterType)> BuiltInRouterAliases()
        {
            var akkaAssembly = typeof(ActorSystem).Assembly;
            var typeMapping = ConfigurationFactory.Default().GetConfig("akka.actor.router.type-mapping");

            return typeMapping.Root.GetObject().Items
                .Select(kvp => (kvp.Key, TypeName: typeMapping.GetString(kvp.Key)))
                .Select(mapping => (mapping.Key, RouterType: akkaAssembly.GetType(mapping.TypeName)))
                .Where(mapping => mapping.RouterType is not null)
                .Select(mapping => (Alias: mapping.Key, RouterType: mapping.RouterType!))
                .ToList();
        }

        [Fact(DisplayName = "Deployer should resolve every shipped router alias and every spelling of its type when dynamic type loading is off")]
        public async Task Should_resolve_every_built_in_router_When_dynamic_type_loading_is_disabled()
        {
            // guards a router going missing from Deployer.BuiltInRouterConfigs: the counts below fail if
            // akka.conf gains an alias nobody added to the table, and the deployments fail if a spelling of
            // an existing one stops matching
            var routers = BuiltInRouterAliases();
            routers.Should().HaveCount(14,
                "akka.conf maps 14 aliases onto routers inside Akka.dll - a new one also needs an entry in Deployer.BuiltInRouterConfigs");

            // from-code is the one alias CreateRouterConfig short-circuits, so NoRouter never reaches the
            // table and the generated aliases below cannot use it
            var tableBacked = routers.Where(router => router.RouterType != typeof(NoRouter)).ToList();
            tableBacked.Should().HaveCount(13, "every alias except from-code resolves through the table");

            // four deployments per table-backed router: the shipped alias (bare "Ns.T"), plus generated
            // aliases for "Ns.T, Akka", for the full assembly-qualified name Akka.Hosting writes, and for an
            // assembly-qualified name pinned to a version this build is not - which only resolves because the
            // lookup strips the assembly identity first
            var cases = routers
                .Select(router => (Alias: router.Alias, Deployment: $"shipped-{router.Alias}", TypeName: (string?)null, router.RouterType))
                .Concat(tableBacked.SelectMany((router, index) => new[]
                {
                    (Alias: $"qualified-{index}", Deployment: $"qualified-{index}",
                        TypeName: (string?)$"{router.RouterType.FullName}, Akka", router.RouterType),
                    (Alias: $"aqn-{index}", Deployment: $"aqn-{index}",
                        TypeName: (string?)router.RouterType.AssemblyQualifiedName, router.RouterType),
                    (Alias: $"stale-aqn-{index}", Deployment: $"stale-aqn-{index}",
                        TypeName: (string?)$"{router.RouterType.FullName}, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null",
                        router.RouterType)
                }))
                .ToList();

            var typeMapping = string.Join(Environment.NewLine, cases
                .Where(c => c.TypeName is not null)
                .Select(c => $"                  {c.Alias} = \"{c.TypeName}\""));

            var deployments = string.Join(Environment.NewLine, cases.Select(c => $@"
                  /{c.Deployment} {{
                    router = {c.Alias}
                    # tail-chopping-* have no default for this one
                    tail-chopping-router.interval = 10ms
                  }}"));

            var config = ConfigurationFactory.ParseString($@"
                akka.actor.router.type-mapping {{
{typeMapping}
                }}
                akka.actor.deployment {{{deployments}
                }}");

            await WithDynamicTypeLoading(false, async () =>
            {
                // the Deployer parses every deployment while the system boots, so a router that is missing
                // from the built-in table fails this line rather than the assertions below
                var system = ActorSystem.Create("built-in-routers-off", config);
                try
                {
                    var deployer = ((ActorSystemImpl)system).Provider.Deployer;
                    foreach (var c in cases)
                    {
                        var deploy = deployer.Lookup(new[] { c.Deployment });
                        deploy.Should().NotBeNull($"[{c.Alias}] should have been deployed");
                        deploy.RouterConfig.Should().BeOfType(c.RouterType,
                            $"[{c.Alias}] names [{c.TypeName ?? c.RouterType.FullName}]");
                    }
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        [Fact(DisplayName = "Deployer should reject a router type-mapping that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_router_is_not_built_in_and_dynamic_type_loading_is_disabled()
        {
            await WithDynamicTypeLoading(false, () =>
            {
                var config = CustomRouterConfig();

                var exception = Assert.Throws<ConfigurationException>(
                    () => ActorSystem.Create("custom-router-off", config));

                exception.Message.Should().Contain("akka.actor.router.type-mapping.my-router");
                exception.Message.Should().Contain(CustomRouterTypeName);
                exception.Message.Should().Contain(SwitchName);
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// Points an alias that Akka.NET does not ship at a router outside Akka.dll.
        /// </summary>
        private static Config CustomRouterConfig()
            => ConfigurationFactory.ParseString($@"
                akka.actor.router.type-mapping.my-router = ""{CustomRouterTypeName}, Akka.Tests""
                akka.actor.deployment {{
                  /custom {{
                    router = my-router
                  }}
                }}");

        [Fact(DisplayName = "Deployer should honor a shipped router alias that user HOCON remaps to another type")]
        public async Task Should_resolve_the_remapped_type_When_a_shipped_alias_points_elsewhere()
        {
            var config = ConfigurationFactory.ParseString($@"
                akka.actor.router.type-mapping.round-robin-pool = ""{CustomRouterTypeName}, Akka.Tests""
                akka.actor.deployment {{
                  /remapped {{
                    router = round-robin-pool
                  }}
                }}");

            await WithDynamicTypeLoading(true, async () =>
            {
                var system = ActorSystem.Create("remapped-router-on", config);
                try
                {
                    var deploy = ((ActorSystemImpl)system).Provider.Deployer.Lookup(new[] { "remapped" });
                    deploy.RouterConfig.Should().BeOfType<DelegatingTestRouter>(
                        "the built-in table is keyed on the mapped type name, so remapping an alias still wins");
                }
                finally
                {
                    await system.Terminate();
                }
            });
        }

        /// <summary>
        /// Every spelling of the two built-in configurators. The assembly-qualified names carry the assembly
        /// version, so they cannot be <c>InlineData</c> constants; the <c>Version=99.0.0.0</c> rows only
        /// resolve because the lookup strips the assembly identity before matching.
        /// </summary>
        public static IEnumerable<object[]> BuiltInConfiguratorNames()
        {
            yield return ["Akka.Actor.DefaultSupervisorStrategy", typeof(DefaultSupervisorStrategy)];
            yield return ["Akka.Actor.DefaultSupervisorStrategy, Akka", typeof(DefaultSupervisorStrategy)];
            yield return [typeof(DefaultSupervisorStrategy).AssemblyQualifiedName!, typeof(DefaultSupervisorStrategy)];
            yield return [
                "Akka.Actor.DefaultSupervisorStrategy, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null",
                typeof(DefaultSupervisorStrategy)];
            yield return ["Akka.Actor.StoppingSupervisorStrategy", typeof(StoppingSupervisorStrategy)];
            yield return ["Akka.Actor.StoppingSupervisorStrategy, Akka", typeof(StoppingSupervisorStrategy)];
            yield return [typeof(StoppingSupervisorStrategy).AssemblyQualifiedName!, typeof(StoppingSupervisorStrategy)];
            yield return [
                "Akka.Actor.StoppingSupervisorStrategy, Akka, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null",
                typeof(StoppingSupervisorStrategy)];
        }

        [Theory(DisplayName = "SupervisorStrategyConfigurator should resolve the built-in configurators when dynamic type loading is off")]
        [MemberData(nameof(BuiltInConfiguratorNames))]
        public async Task Should_resolve_the_built_in_supervisor_strategy_configurators_When_dynamic_type_loading_is_disabled(
            string typeName, Type expected)
        {
            await WithDynamicTypeLoading(false, () =>
            {
                SupervisorStrategyConfigurator.CreateConfigurator(typeName).Should().BeOfType(expected);
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "SupervisorStrategyConfigurator should resolve a configurator that is not built in when dynamic type loading is on")]
        public async Task Should_resolve_a_custom_supervisor_strategy_configurator_When_dynamic_type_loading_is_enabled()
        {
            await WithDynamicTypeLoading(true, () =>
            {
                SupervisorStrategyConfigurator
                    .CreateConfigurator($"{CustomSupervisorStrategyTypeName}, Akka.Tests")
                    .Should().BeOfType<Akka.Tests.Actor.TestStrategy>();
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "SupervisorStrategyConfigurator should reject a configurator that is not built in when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_supervisor_strategy_configurator_is_not_built_in_and_dynamic_type_loading_is_disabled()
        {
            await WithDynamicTypeLoading(false, () =>
            {
                // the public overload cannot tell which setting the name came from, so it names both
                var both = Assert.Throws<ConfigurationException>(
                    () => SupervisorStrategyConfigurator.CreateConfigurator($"{CustomSupervisorStrategyTypeName}, Akka.Tests"));

                both.Message.Should().Contain("akka.actor.guardian-supervisor-strategy / supervisor-strategy");
                both.Message.Should().Contain(CustomSupervisorStrategyTypeName);
                both.Message.Should().Contain(SwitchName);

                // a caller that knows its setting gets that setting named instead
                var named = Assert.Throws<ConfigurationException>(
                    () => SupervisorStrategyConfigurator.CreateConfigurator(
                        $"{CustomSupervisorStrategyTypeName}, Akka.Tests", "akka.persistence.journal.inmem.supervisor-strategy"));

                named.Message.Should().Contain("akka.persistence.journal.inmem.supervisor-strategy");
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "SupervisorStrategyConfigurator should still reject a null type name when dynamic type loading is off")]
        public async Task Should_throw_ConfigurationException_When_the_supervisor_strategy_configurator_is_null()
        {
            // guards the null arm surviving the switch-to-dictionary conversion: a dictionary lookup on null
            // throws ArgumentNullException, so the explicit null check has to come first
            await WithDynamicTypeLoading(false, () =>
            {
                var exception = Assert.Throws<ConfigurationException>(
                    () => SupervisorStrategyConfigurator.CreateConfigurator(null));

                exception.Message.Should().Contain("typeName is null");
                return Task.CompletedTask;
            });
        }
    }
}
