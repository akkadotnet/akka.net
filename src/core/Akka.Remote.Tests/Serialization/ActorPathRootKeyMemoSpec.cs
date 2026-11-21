#nullable enable

using System;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Serialization;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Serialization;

public sealed class ActorPathRootKeyMemoSpec
{
    private const string Root = "akka://system@127.0.0.1:2552/";
    private static readonly FieldInfo Memo = typeof(ActorPathCache).GetField("_lastRootKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo Epoch = typeof(LruBoundedCache<string, ActorPath>).GetField("_epoch", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static string? Key(ActorPathCache cache) => (string?)Memo.GetValue(cache);

    [Theory(DisplayName = "Should_PreserveParsedPath_When_RootKeyIsReused")]
    [InlineData("akka://system")]
    [InlineData("akka://system/")]
    [InlineData("akka://system/user/one#2147483647")]
    [InlineData("akka://system/user/two#-2147483648")]
    [InlineData("akka://system@127.0.0.1:2552/temp/one#42")]
    [InlineData("akka://system@[::1]:2552/user/one#17")]
    [InlineData("akka.tcp://system@localhost:2552/user/one#17")]
    public void Should_preserve_parsed_path_when_root_key_is_reused(string input)
    {
        var cache = new ActorPathCache();
        ActorPath.TryParse(input, out var expected).Should().BeTrue();
        cache.GetOrCompute(input).ToSerializationFormat().Should().Be(expected!.ToSerializationFormat());
        // A different suffix forces Compute, even though the root key is already memoized.
        ActorPath.TryParseParts(input.AsSpan(), out var address, out _).Should().BeTrue();
        var next = address.ToString() + "/user/child#23";
        ActorPath.TryParse(next, out expected).Should().BeTrue();
        cache.GetOrCompute(next).ToSerializationFormat().Should().Be(expected!.ToSerializationFormat());
    }

    [Fact(DisplayName = "Should_ReuseOnlyRootString_When_OneUseSuffixesChange")]
    public void Should_reuse_only_root_string_when_one_use_suffixes_change()
    {
        var cache = new ActorPathCache();
        cache.GetOrCompute(Root + "temp/first#1");
        var key = Key(cache);
        key.Should().Be(Root);
        var epoch = (int)Epoch.GetValue(cache)!;
        cache.GetOrCompute(Root + "temp/second#2").Uid.Should().Be(2);
        Key(cache).Should().BeSameAs(key);
        unchecked { ((int)Epoch.GetValue(cache)!).Should().Be(epoch + 1); }
        cache.TryGet(Root, out _).Should().BeTrue();
        cache.Stats.Entries.Should().Be(3);
    }

    [Fact(DisplayName = "Should_CompareExactRawRoot_When_AddressesAlternateOrAlias")]
    public void Should_compare_exact_raw_root_when_addresses_alternate_or_alias()
    {
        var cache = new ActorPathCache();
        foreach (var root in new[] { Root, "akka://other@127.0.0.1:2552/", "akka://system@localhost:2552/", Root })
        {
            var input = root + "user/next#7";
            cache.GetOrCompute(input).ToSerializationFormat().Should().Be(ActorPath.Parse(input).ToSerializationFormat());
            // Force Compute even on the final repeated full path.
            cache.GetOrCompute(root + "temp/" + Guid.NewGuid().ToString("N"));
            Key(cache).Should().Be(root);
        }
    }

    [Theory(DisplayName = "Should_RejectInvalidAddress_WithoutReplacingValidRootMemo")]
    [InlineData("relative/path")]
    [InlineData("akka:/system/user/a")]
    [InlineData("akka://system@localhost:not-a-port/user/a")]
    public void Should_reject_invalid_address_without_replacing_valid_root_memo(string input)
    {
        var cache = new ActorPathCache();
        cache.GetOrCompute(Root + "user/good#1");
        var previous = Key(cache);
        cache.GetOrCompute(input).Should().BeNull();
        Key(cache).Should().BeSameAs(previous);
        cache.GetOrCompute(Root + "user/after#2").Uid.Should().Be(2);
    }

    [Fact(DisplayName = "Should_PreserveUidParseFailure_When_ValidRootIsMemoized")]
    public void Should_preserve_uid_parse_failure_when_valid_root_is_memoized()
    {
        var cache = new ActorPathCache();
        cache.GetOrCompute(Root + "user/good#1");
        Action parse = () => cache.GetOrCompute(Root + "user/bad#not-a-number");
        parse.Should().Throw<FormatException>();
        cache.GetOrCompute(Root + "user/good#2").Uid.Should().Be(2);
    }

    [Fact(DisplayName = "Should_RebuildEvictedRoot_When_OnlyItsStringMemoSurvives")]
    public void Should_rebuild_evicted_root_when_only_its_string_memo_survives()
    {
        var cache = new ActorPathCache(4, 0);
        cache.GetOrCompute(Root + "user/first");
        cache.TryGet(Root, out var original).Should().BeTrue();
        var key = Key(cache);
        var other = new RootActorPath(new Address("akka", "other"));
        for (var i = 0; i < 64 && cache.TryGet(Root, out _); i++)
            cache.TrySet("pressure-" + i, other);
        cache.TryGet(Root, out _).Should().BeFalse();
        Key(cache).Should().BeSameAs(key);
        cache.GetOrCompute(Root + "user/after#3").Uid.Should().Be(3);
        cache.TryGet(Root, out var rebuilt).Should().BeTrue();
        rebuilt.Should().NotBeSameAs(original);
        rebuilt.Address.Should().Be(original.Address);
        Key(cache).Should().BeSameAs(key);
    }

    [Fact(DisplayName = "Should_HandleLargeRootAndNoSlash_WithoutRetainingSuffix")]
    public void Should_handle_large_root_and_no_slash_without_retaining_suffix()
    {
        var address = "akka://" + new string('a', 1100);
        var cache = new ActorPathCache();
        cache.GetOrCompute(address).Address.System.Should().Be(new string('a', 1100));
        var key = Key(cache);
        key.Should().Be(address + "/");
        cache.GetOrCompute(address + "/user/next#19").Uid.Should().Be(19);
        Key(cache).Should().BeSameAs(key);
        Key(new ActorPathCache()).Should().BeNull();
    }

    [Fact(DisplayName = "Should_KeepMemoPerThread_When_UsingThreadLocalExtension")]
    public async Task Should_keep_memo_per_thread_when_using_thread_local_extension()
    {
        var extension = new ActorPathThreadLocalCache();
        var first = await Task.Factory.StartNew(() =>
        {
            var cache = extension.Cache;
            cache.GetOrCompute(Root + "user/first");
            return cache;
        }, default, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var second = await Task.Factory.StartNew(() =>
        {
            var cache = extension.Cache;
            cache.GetOrCompute("akka://other/user/second");
            return cache;
        }, default, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        first.Should().NotBeSameAs(second);
        Key(first).Should().Be(Root);
        Key(second).Should().Be("akka://other/");
    }
}
