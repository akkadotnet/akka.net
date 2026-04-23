//-----------------------------------------------------------------------
// <copyright file="ParallelAmbientContextSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.TestKit.Xunit.Attributes;
using Xunit;

namespace Akka.TestKit.Xunit.Tests;

// Regression guard for the xUnit v3 parallel-class implicit-sender leak
// (see ActorCellKeepingSynchronizationContext for the mechanism).
//
// This project provides its own xunit.runner.json with parallel collections
// enabled so CI and local runs exercise the reported failure mode by default.

public abstract class ParallelAmbientContextSpecBase : TestKit, IAsyncLifetime
{
    // Forces the post-ctor continuation onto a different SC worker — the
    // thread pollution only manifests when the body thread differs from the
    // ctor thread.
    public async ValueTask InitializeAsync() => await Task.Yield();

    public ValueTask DisposeAsync() => default;

    [Fact]
    public async Task Implicit_sender_should_resolve_to_own_TestActor()
    {
        // Pre-await prefix — before the fix, this window read whatever cell a
        // sibling ctor pinned on the body thread.
        TestActor.Tell("ping");
        await ExpectMsgAsync<string>("ping", TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(TestActor, LastSender);

        await Task.Yield();
        TestActor.Tell("ping-after-yield");
        await ExpectMsgAsync<string>("ping-after-yield", TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(TestActor, LastSender);
    }
}

public class ParallelAmbientContextSpec01 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec02 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec03 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec04 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec05 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec06 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec07 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec08 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec09 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec10 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec11 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec12 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec13 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec14 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec15 : ParallelAmbientContextSpecBase { }
public class ParallelAmbientContextSpec16 : ParallelAmbientContextSpecBase { }

// Regression test for INoImplicitSender under the same xUnit v3 parallel
// scheduling. INoImplicitSender tests contractually have no implicit sender —
// Current must be null both at body entry (pre-await prefix) and across any
// await continuations resumed on a reused worker thread that a sibling's
// Before hook may have pinned with a non-null cell.
public abstract class ParallelNoImplicitSenderSpecBase : TestKit, IAsyncLifetime, INoImplicitSender
{
    public async ValueTask InitializeAsync() => await Task.Yield();

    public ValueTask DisposeAsync() => default;

    [Fact]
    public async Task Current_should_be_null_both_pre_and_post_await()
    {
        // Invariant: body must enter with Current == null.
        Assert.Null(InternalCurrentActorCellKeeper.Current);

        // Force continuation onto a potentially polluted worker.
        await Task.Yield();
        Assert.Null(InternalCurrentActorCellKeeper.Current);

        await Task.Yield();
        Assert.Null(InternalCurrentActorCellKeeper.Current);
    }
}

public class ParallelNoImplicitSenderSpec01 : ParallelNoImplicitSenderSpecBase { }
public class ParallelNoImplicitSenderSpec02 : ParallelNoImplicitSenderSpecBase { }
public class ParallelNoImplicitSenderSpec03 : ParallelNoImplicitSenderSpecBase { }
public class ParallelNoImplicitSenderSpec04 : ParallelNoImplicitSenderSpecBase { }
public class ParallelNoImplicitSenderSpec05 : ParallelNoImplicitSenderSpecBase { }
public class ParallelNoImplicitSenderSpec06 : ParallelNoImplicitSenderSpecBase { }
public class ParallelNoImplicitSenderSpec07 : ParallelNoImplicitSenderSpecBase { }
public class ParallelNoImplicitSenderSpec08 : ParallelNoImplicitSenderSpecBase { }
