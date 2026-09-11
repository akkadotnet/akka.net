//-----------------------------------------------------------------------
// <copyright file="ParallelAmbientContextSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.TestActors;
using TUnit.Core;

namespace Akka.TestKit.TUnit.Tests;

public abstract class ParallelAmbientContextSpecBase : Akka.TestKit.TUnit.TestKit
{
    [Test]
    public async Task Should_keep_its_own_implicit_sender_across_awaits()
    {
        var actor = Sys.ActorOf(SimpleEchoActor.Props());
        await Task.Yield();

        actor.Tell(GetType().Name);

        await ExpectMsgAsync(GetType().Name, TimeSpan.FromSeconds(3));
    }
}

[InheritsTests]
public sealed class ParallelAmbientContextSpec1 : ParallelAmbientContextSpecBase;
[InheritsTests]
public sealed class ParallelAmbientContextSpec2 : ParallelAmbientContextSpecBase;
[InheritsTests]
public sealed class ParallelAmbientContextSpec3 : ParallelAmbientContextSpecBase;
[InheritsTests]
public sealed class ParallelAmbientContextSpec4 : ParallelAmbientContextSpecBase;
