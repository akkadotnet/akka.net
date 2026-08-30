//-----------------------------------------------------------------------
// <copyright file="ActorCellKeepingSynchronizationContextSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
using System.Threading;
using Akka.TestKit;
using Xunit;

namespace Akka.TestKit.Xunit.Tests;

public class ActorCellKeepingSynchronizationContextSpec
{
    [Fact]
    public void Post_should_delegate_to_outer_synchronization_context()
    {
        var inner = new RecordingSynchronizationContext();
        var wrapper = CreateWrapper(inner);
        var state = new object();
        object? callbackState = null;
        SynchronizationContext? callbackContext = null;

        wrapper.Post(s =>
        {
            callbackState = s;
            callbackContext = SynchronizationContext.Current;
        }, state);

        Assert.Equal(1, inner.PostCalls);
        Assert.Same(state, callbackState);
        Assert.Same(wrapper, callbackContext);
    }

    [Fact]
    public void Send_should_delegate_to_outer_synchronization_context()
    {
        var inner = new RecordingSynchronizationContext();
        var wrapper = CreateWrapper(inner);
        var state = new object();
        object? callbackState = null;
        SynchronizationContext? callbackContext = null;

        wrapper.Send(s =>
        {
            callbackState = s;
            callbackContext = SynchronizationContext.Current;
        }, state);

        Assert.Equal(1, inner.SendCalls);
        Assert.Same(state, callbackState);
        Assert.Same(wrapper, callbackContext);
    }

    [Fact]
    public void CreateCopy_should_preserve_outer_synchronization_context()
    {
        var inner = new RecordingSynchronizationContext();
        var wrapper = CreateWrapper(inner);
        var copy = wrapper.CreateCopy();

        copy.Post(_ => { }, null);
        copy.Send(_ => { }, null);

        Assert.Equal(1, inner.PostCalls);
        Assert.Equal(1, inner.SendCalls);
    }

    private static SynchronizationContext CreateWrapper(SynchronizationContext? inner)
    {
        var wrapperType = typeof(TestKitBase).Assembly.GetType("Akka.TestKit.ActorCellKeepingSynchronizationContext", throwOnError: true)!;
        return (SynchronizationContext)Activator.CreateInstance(
            wrapperType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [null, inner],
            culture: null)!;
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public int PostCalls { get; private set; }
        public int SendCalls { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCalls++;
            d(state);
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            SendCalls++;
            d(state);
        }
    }
}
