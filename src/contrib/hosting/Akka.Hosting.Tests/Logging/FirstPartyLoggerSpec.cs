// -----------------------------------------------------------------------
//  <copyright file="FirstPartyLoggerSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Hosting.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Akka.Hosting.Tests.Logging;

/// <summary>
/// AppContext switches are process-wide, so a spec that flips Akka.DynamicTypeLoading never runs beside another.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DynamicTypeLoadingCollection
{
    public const string Name = "Akka.DynamicTypeLoading";
}

[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class FirstPartyLoggerSpec
{
    private const string SwitchName = "Akka.DynamicTypeLoading";

    private readonly ITestOutputHelper _output;

    public FirstPartyLoggerSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    // A whole Hosting app does not start with the switch off yet (Akka.Streams' serializer rows), so this boots
    // a plain ActorSystem on the akka.loggers value AddLoggerFactory() writes: the full AssemblyQualifiedName.
    [Theory(DisplayName = "LoggingBus should start LoggerFactoryLogger, as the type reflection resolves, with dynamic type loading on or off")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_start_LoggerFactoryLogger_When_named_as_AddLoggerFactory_writes_it(bool dynamicTypeLoading)
    {
        var name = typeof(LoggerFactoryLogger).AssemblyQualifiedName!;
        await WithDynamicTypeLoading(dynamicTypeLoading, async () =>
        {
            var system = ActorSystem.Create("first-party-logger", SetupFor(name));
            try
            {
                (await LoggerTypeOf(system)).Should().Be(Type.GetType(name, throwOnError: true));
            }
            finally
            {
                await system.Terminate();
            }
        });
    }

    [Theory(DisplayName = "LoggingBus should resolve every spelling of LoggerFactoryLogger that names Akka.Hosting when dynamic type loading is off")]
    [InlineData("Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting")]
    [InlineData("Akka.Hosting.Logging.LoggerFactoryLogger,akka.hosting")]
    [InlineData("Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null")]
    public async Task Should_start_LoggerFactoryLogger_When_spelled_differently(string name)
    {
        await WithDynamicTypeLoading(false, async () =>
        {
            var system = ActorSystem.Create("first-party-logger-spelling", SetupFor(name));
            try
            {
                (await LoggerTypeOf(system)).Should().Be(typeof(LoggerFactoryLogger));
            }
            finally
            {
                await system.Terminate();
            }
        });
    }

    // Version skew is the real scenario: HOCON written against one Akka.Hosting build still has to resolve
    // against whatever build is actually loaded. This also proves the first-party table runs even when dynamic
    // type loading is on, where a version-pinned name would otherwise be handed to the general-purpose resolver.
    [Fact(DisplayName = "LoggingBus should resolve a version-skewed LoggerFactoryLogger name when dynamic type loading is on")]
    public async Task Should_start_LoggerFactoryLogger_When_version_skewed_and_dynamic_type_loading_is_enabled()
    {
        const string name = "Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Hosting, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null";
        await WithDynamicTypeLoading(true, async () =>
        {
            var system = ActorSystem.Create("first-party-logger-version-skew-on", SetupFor(name));
            try
            {
                (await LoggerTypeOf(system)).Should().Be(typeof(LoggerFactoryLogger));
            }
            finally
            {
                await system.Terminate();
            }
        });
    }

    [Theory(DisplayName = "LoggingBus should reject a logger that is not first-party when dynamic type loading is off")]
    [InlineData("Akka.Hosting.Logging.LoggerFactoryLogger")] // no assembly: Type.GetType from Akka.dll would not find it
    [InlineData("Akka.Hosting.Logging.LoggerFactoryLogger, Akka.Remote")]
    [InlineData("Akka.Hosting.Logging.LoggerFactoryLoggerX, Akka.Hosting")]
    [InlineData("Akka.Hosting.Tests.Logging.FirstPartyLoggerSpec+OtherLogger, Akka.Hosting.Tests")]
    public async Task Should_throw_ConfigurationException_When_logger_is_not_first_party(string name)
    {
        await WithDynamicTypeLoading(false, () =>
        {
            var exception = Assert.Throws<ConfigurationException>(
                () => ActorSystem.Create("not-first-party-logger", SetupFor(name)));

            exception.Message.Should().Contain("akka.loggers").And.Contain(name).And.Contain(SwitchName);
            return Task.CompletedTask;
        });
    }

    public sealed class OtherLogger : LoggerFactoryLogger
    {
    }

    private static ActorSystemSetup SetupFor(string loggerName)
        => BootstrapSetup.Create()
            .WithConfig(ConfigurationFactory.ParseString($"akka.loggers = [\"{loggerName}\"]"))
            .And(new LoggerFactorySetup(NullLoggerFactory.Instance));

    /// <summary>The type the logging bus started its logger actor from.</summary>
    private static async Task<Type> LoggerTypeOf(ActorSystem system)
    {
        var logger = await system.ActorSelection($"/system/log*-{nameof(LoggerFactoryLogger)}")
            .ResolveOne(TimeSpan.FromSeconds(3));
        return ((ActorRefWithCell)logger).Underlying.Props.Type;
    }

    private static async Task WithDynamicTypeLoading(bool enabled, Func<Task> body)
    {
        var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
        AppContext.SetSwitch(SwitchName, enabled);
        try
        {
            await body();
        }
        finally
        {
            AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
        }
    }
}
