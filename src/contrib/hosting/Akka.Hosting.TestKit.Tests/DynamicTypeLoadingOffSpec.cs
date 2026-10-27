// -----------------------------------------------------------------------
//  <copyright file="DynamicTypeLoadingOffSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Hosting.TestKit.Tests;

/// <summary>
/// AppContext switches are process-wide, so a spec that flips Akka.DynamicTypeLoading never runs beside
/// another. This assembly runs its other collections in parallel; a collection that disables parallelization
/// runs on its own.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DynamicTypeLoadingCollection
{
    public const string Name = "Akka.DynamicTypeLoading";
}

/// <summary>
/// Akka.Hosting.TestKit prepends Akka.TestKit's default HOCON, which names <c>TestEventListener</c> and
/// <c>CallingThreadDispatcherConfigurator</c> by type. Both have to work with the switch off.
/// </summary>
[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class DynamicTypeLoadingOffSpec
{
    private const string SwitchName = "Akka.DynamicTypeLoading";

    private sealed class SwitchOffKit : TestKit
    {
        public SwitchOffKit() : base("switch-off")
        {
        }

        protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
        {
        }
    }

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

    [Fact(DisplayName = "Hosting TestKit should start and deliver to the test actor when dynamic type loading is off")]
    public async Task Should_start_Hosting_TestKit_When_dynamic_type_loading_is_off()
    {
        await WithDynamicTypeLoadingOff(async () =>
        {
            var kit = new SwitchOffKit();
            await kit.InitializeAsync();
            try
            {
                kit.TestActor.Tell("hello");
                await kit.ExpectMsgAsync("hello", TimeSpan.FromSeconds(3));

                kit.Sys.Dispatchers.Lookup(CallingThreadDispatcher.Id).Should().BeOfType<CallingThreadDispatcher>();
            }
            finally
            {
                await kit.DisposeAsync();
            }
        });
    }

    [Fact(DisplayName = "Hosting TestKit EventFilter should intercept log events when dynamic type loading is off")]
    public async Task Should_intercept_log_events_When_dynamic_type_loading_is_off()
    {
        await WithDynamicTypeLoadingOff(async () =>
        {
            var kit = new SwitchOffKit();
            await kit.InitializeAsync();
            try
            {
                await kit.EventFilter.Info(contains: "marker-message")
                    .ExpectOneAsync(() =>
                    {
                        kit.Sys.Log.Info("marker-message");
                        return Task.CompletedTask;
                    });
            }
            finally
            {
                await kit.DisposeAsync();
            }
        });
    }
}
