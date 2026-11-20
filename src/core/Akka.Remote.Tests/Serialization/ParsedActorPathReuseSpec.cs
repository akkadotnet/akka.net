//-----------------------------------------------------------------------
// <copyright file="ParsedActorPathReuseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Serialization;

public sealed class ParsedActorPathReuseSpec : AkkaSpec
{
    private static readonly Config Config = ConfigurationFactory.ParseString("""
        akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
        akka.remote.artery.enabled = on
        akka.remote.artery.canonical.hostname = "127.0.0.1"
        akka.remote.artery.canonical.port = 0
        """);

    public ParsedActorPathReuseSpec(ITestOutputHelper output) : base(Config, output)
    {
    }
    private RemoteActorRefProvider Provider => (RemoteActorRefProvider)((ExtendedActorSystem)Sys).Provider;

    [Theory(DisplayName = "Should_ResolveCurrentFutureRef_When_TheParsedPathOutlivesItsRegistration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_resolve_current_future_ref_when_the_parsed_path_outlives_its_registration(bool internalEntry)
    {
        var provider = Provider;
        var first = provider.CreateFutureRef<string>(new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
        var path = first.Path;
        var wirePath = path.ToSerializationFormatWithAddress(provider.DefaultAddress);
        ActorPathThreadLocalCache.For(Sys).Cache.GetOrCompute(wirePath);
        IActorRef Resolve() => internalEntry ? provider.InternalResolveActorRef(wirePath)
            : provider.ResolveActorRefWithLocalAddress(wirePath, provider.DefaultAddress);
        var deadLetters = CreateTestProbe();
        Sys.EventStream.Subscribe(deadLetters.Ref, typeof(DeadLetter));
        provider.RegisterTempActor(first, path);
        try
        {
            Resolve().Should().BeSameAs(first);
            ActorRefResolveThreadLocalCache.For(Sys).Cache.TryGet(wirePath, out _).Should().BeFalse();
            provider.UnregisterTempActor(path);
            var missing = Resolve();
            missing.Should().BeOfType<EmptyLocalActorRef>();
            missing.Tell("late-reply", TestActor);
            (await deadLetters.ExpectMsgAsync<DeadLetter>()).Message.Should().Be("late-reply");

            var replacement = new FutureActorRef<string>(new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously), path, provider);
            provider.RegisterTempActor(replacement, path);
            ActorPathThreadLocalCache.For(Sys).Cache.GetOrCompute(wirePath);
            Resolve().Should().BeSameAs(replacement);
            ActorRefResolveThreadLocalCache.For(Sys).Cache.TryGet(wirePath, out _).Should().BeFalse();
        }
        finally
        {
            provider.UnregisterTempActor(path);
            Sys.EventStream.Unsubscribe(deadLetters.Ref);
        }
    }

    [Theory(DisplayName = "Should_PreserveUidAndExistingRefCacheSemantics_When_AnActorNameIsReused")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_preserve_uid_and_existing_ref_cache_semantics_when_an_actor_name_is_reused(bool cacheReference)
    {
        var provider = Provider;
        var first = Sys.ActorOf(Props.Empty, "reused");
        var oldPath = first.Path.ToSerializationFormatWithAddress(provider.DefaultAddress);
        ActorPathThreadLocalCache.For(Sys).Cache.GetOrCompute(oldPath);
        if (cacheReference)
            provider.ResolveActorRef(oldPath).Should().BeSameAs(first);
        await WatchAsync(first);
        Sys.Stop(first);
        await ExpectTerminatedAsync(first);
        var replacement = Sys.ActorOf(Props.Empty, "reused");
        if (cacheReference)
        {
            // Awaiting termination can resume on another thread. Populate that thread's
            // existing cache so this case exercises a cache hit with the old incarnation.
            ActorRefResolveThreadLocalCache.For(Sys).Cache.TrySet(oldPath, first).Should().BeTrue();
        }
        var resolvedOld = provider.ResolveActorRefWithLocalAddress(oldPath, provider.DefaultAddress);
        resolvedOld.Should().NotBeSameAs(replacement);
        if (cacheReference)
            resolvedOld.Should().BeSameAs(first);
        else
            resolvedOld.Should().BeOfType<EmptyLocalActorRef>();
        provider.ResolveActorRefWithLocalAddress(replacement.Path.ToSerializationFormatWithAddress(provider.DefaultAddress),
            provider.DefaultAddress).Should().BeSameAs(replacement);
    }

    [Fact(DisplayName = "Should_PreserveAddressesRootAndSelection_When_AParsedPathIsCached")]
    public async Task Should_preserve_addresses_root_and_selection_when_a_parsed_path_is_cached()
    {
        var provider = Provider;
        foreach (var address in new[] { provider.RootPath.Address, provider.DefaultAddress })
        {
            var path = TestActor.Path.ToSerializationFormatWithAddress(address);
            ActorPathThreadLocalCache.For(Sys).Cache.GetOrCompute(path);
            provider.InternalResolveActorRef(path).Should().BeSameAs(TestActor);
            provider.ResolveActorRefWithLocalAddress(path, provider.DefaultAddress).Should().BeSameAs(TestActor);
            (await Sys.ActorSelection(path).ResolveOne(TimeSpan.FromSeconds(3))).Should().BeSameAs(TestActor);
            var root = new RootActorPath(address).ToSerializationFormat();
            provider.ResolveActorRefWithLocalAddress(root, provider.DefaultAddress).Should().BeSameAs(provider.RootGuardian);
            provider.InternalResolveActorRef(root).Should().BeSameAs(provider.DeadLetters);
        }
        var foreignAddress = new Address("akka", "AnotherSystem", "127.0.0.1", 25555);
        var foreign = (new RootActorPath(foreignAddress) / "user" / "other").ToSerializationFormat();
        ActorPathThreadLocalCache.For(Sys).Cache.GetOrCompute(foreign);
        var remote = provider.InternalResolveActorRef(foreign);
        remote.Should().BeAssignableTo<IRemoteRef>();
        remote.Path.Address.Should().Be(foreignAddress);
    }

    [Fact(DisplayName = "Should_PreserveNullEmptyIgnoreAndMalformedPathBehavior")]
    public void Should_preserve_null_empty_ignore_and_malformed_path_behavior()
    {
        var provider = Provider;
        provider.InternalResolveActorRef(null!).Should().BeSameAs(provider.DeadLetters);
        provider.InternalResolveActorRef("").Should().BeSameAs(ActorRefs.NoSender);
        provider.ResolveActorRefWithLocalAddress(null!, provider.DefaultAddress).Should().BeSameAs(provider.DeadLetters);
        provider.ResolveActorRef(provider.IgnoreRef.Path.ToString()).Should().BeSameAs(provider.IgnoreRef);
        foreach (var malformed in new[] { "invalid", "relative/path", "akka:/missing-slash" })
        {
            provider.InternalResolveActorRef(malformed).Should().BeSameAs(provider.DeadLetters);
            Action resolveWithLocal = () => provider.ResolveActorRefWithLocalAddress(malformed, provider.DefaultAddress);
            resolveWithLocal.Should().Throw<NullReferenceException>();
        }
    }

    [Fact(DisplayName = "Should_ParseNormally_When_ThePathCacheIsNotInitialized")]
    public void Should_parse_normally_when_the_path_cache_is_not_initialized()
    {
        var provider = Provider;
        var cacheField = typeof(RemoteActorRefProvider).GetField("_actorPathThreadLocalCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cache = cacheField.GetValue(provider);
        try
        {
            cacheField.SetValue(provider, null);
            provider.InternalResolveActorRef(TestActor.Path.ToSerializationFormatWithAddress(provider.DefaultAddress)).Should().BeSameAs(TestActor);
            provider.InternalResolveActorRef(null!).Should().BeSameAs(provider.DeadLetters);
            provider.InternalResolveActorRef("").Should().BeSameAs(ActorRefs.NoSender);
        }
        finally
        {
            cacheField.SetValue(provider, cache);
        }
    }

    [Fact(DisplayName = "Should_PreserveParsingFallbackWithoutPopulatingThePathCache_When_TheEntryIsAbsent")]
    public void Should_preserve_parsing_fallback_without_populating_the_path_cache_when_the_entry_is_absent()
    {
        var provider = Provider;
        var future = provider.CreateFutureRef<string>(new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
        var path = future.Path.ToSerializationFormatWithAddress(provider.DefaultAddress);
        var cache = ActorPathThreadLocalCache.For(Sys).Cache;
        cache.TryGet(path, out _).Should().BeFalse();
        provider.RegisterTempActor(future, future.Path);
        try
        {
            provider.InternalResolveActorRef(path).Should().BeSameAs(future);
            cache.TryGet(path, out _).Should().BeFalse();
        }
        finally
        {
            provider.UnregisterTempActor(future.Path);
        }
    }
}
