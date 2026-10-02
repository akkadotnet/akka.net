// -----------------------------------------------------------------------
//  <copyright file="ParallelAmbientContextSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.TestKit;
using Xunit;

namespace Akka.Hosting.TestKit.Tests;

public abstract class ParallelAmbientContextSpecBase : TestKit
{
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
    }

    [Fact]
    public async Task Implicit_sender_should_resolve_to_own_TestActor()
    {
        TestActor.Tell("ping");
        await ExpectMsgAsync<string>(
            "ping",
            TimeSpan.FromSeconds(5),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(TestActor, LastSender);

        await Task.Yield();
        TestActor.Tell("ping-after-yield");
        await ExpectMsgAsync<string>(
            "ping-after-yield",
            TimeSpan.FromSeconds(5),
            cancellationToken: TestContext.Current.CancellationToken);
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

public abstract class ParallelNoImplicitSenderSpecBase : TestKit, INoImplicitSender
{
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
    }

    [Fact]
    public async Task Current_should_be_null_both_pre_and_post_await()
    {
        Assert.Null(InternalCurrentActorCellKeeper.Current);
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
