// -----------------------------------------------------------------------
// <copyright file="ArteryQuicResourceLifetimeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Remote.Artery;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public sealed class ArteryQuicResourceLifetimeSpec
    {
        [Fact(DisplayName = "Native stop closes root admission but permits an owned accept handoff without releasing early")]
        public async Task Should_RetainNativeResources_When_AnAcceptHandsOffDuringStop()
        {
            var lifetime = new ArteryQuicResourceLifetime();
            using var listener = lifetime.Acquire();
            using var accepting = lifetime.Acquire();
            lifetime.Stop();
            Assert.Throws<InvalidOperationException>(() => lifetime.Acquire());
            using var connection = accepting.Fork();
            accepting.Dispose();
            listener.Dispose();
            lifetime.WhenReleased.IsCompleted.Should().BeFalse("the accepted connection still owns native resources");
            connection.Dispose();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await lifetime.WhenReleased.WaitAsync(deadline.Token);
            Assert.Throws<InvalidOperationException>(() => accepting.Fork());
        }

        [Fact(DisplayName = "Releasing a native owner twice cannot consume another owner's lifetime")]
        public async Task Should_ReleaseExactlyOnce_When_OwnersAreRetiredRepeatedly()
        {
            var lifetime = new ArteryQuicResourceLifetime();
            using var first = lifetime.Acquire();
            using var second = lifetime.Acquire();
            first.Dispose();
            first.Dispose();
            lifetime.Stop();
            lifetime.WhenReleased.IsCompleted.Should().BeFalse();
            second.Dispose();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await lifetime.WhenReleased.WaitAsync(deadline.Token);
        }

        [Fact(DisplayName = "Native lifetime remains open between resources until admission stops")]
        public async Task Should_CompleteOnlyAfterStop_When_NoNativeOwnerRemains()
        {
            var lifetime = new ArteryQuicResourceLifetime();
            lifetime.Acquire().Dispose();
            lifetime.WhenReleased.IsCompleted.Should().BeFalse("another bind or connection can still start");
            lifetime.Stop();
            lifetime.Stop();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await lifetime.WhenReleased.WaitAsync(deadline.Token);
        }
    }
}
