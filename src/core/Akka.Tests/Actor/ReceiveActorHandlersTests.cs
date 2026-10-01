// -----------------------------------------------------------------------
//  <copyright file="ReceiveActorHandlersTests.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Xunit;

namespace Akka.Tests.Actor;

public class ReceiveActorHandlersTests
{
    [Fact(DisplayName = "Should_Fail_When_AddingAnyHandler_After_ReceiveAnyHandlerAdded")]
    public void Given_ReceiveAnyHandler_Added_When_Adding_Any_Other_Handler_Then_Should_Fail()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddReceiveAnyHandler(_ => { });

        // A ReceiveAny handler has been added, so adding any other handler should fail
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddReceiveAnyHandler(_ => { }));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(object), null, _ => true, alwaysHandles: true));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(int), null, _ => true, alwaysHandles: true));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddGenericReceiveHandler<bool>(null, _ => true, alwaysHandles: true));
    }

    [Fact(DisplayName = "Should_Succeed_When_AddingReceiveAnyHandler_After_TypedReceiveHandlerWithPredicate")]
    public void Given_TypedReceiveHandlerWithPredicate_When_Adding_ReceiveAnyHandler_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true, alwaysHandles: true);

        // As the object handler has a predicate, adding a ReceiveAny handler should be allowed
        // as the object handler might not handle all objects.
        handlers.AddReceiveAnyHandler(_ => { });
    }

    [Fact(DisplayName = "Should_Fail_When_AddingSameTypedReceiveHandler_After_TypedReceiveHandlerWithNoPredicate")]
    public void Given_TypedReceiveHandler_When_Adding_SameTypedReceiveHandler_Then_Should_Fail()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true, alwaysHandles: true);

        // As a handler for the type of object with no predicate is added,
        // adding another handler for the same type combination should fail with an exception
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(object), null, _ => true, alwaysHandles: true));
    }

    [Fact(DisplayName = "Should_Succeed_When_AddingSameTypedReceiveHandlerWithPredicate_After_TypedReceiveHandlerWithPredicate")]
    public void Given_TypedReceiveHandlerWithPredicate_When_Adding_SameTypedReceiveHandlerWithPredicate_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true, alwaysHandles: true);

        // The handler added has a predicate which makes it uncertain if it will handle the message.
        // Adding another handler for the same type combination should be allowed.
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true, alwaysHandles: true);
    }

    [Fact(DisplayName = "Should_Fail_When_AddingAnyOtherReceiveHandler_After_ObjectTypedReceiveHandlerWithNoPredicate")]
    public void Given_ObjectTypedReceiveHandlerWithNoPredicate_When_Adding_Any_Other_ReceiveHandler_Then_Should_Fail()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true, alwaysHandles: true);

        // This should throw because the object handler is already added and would catch this before.
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(int), _ => true, _ => true, alwaysHandles: true));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddGenericReceiveHandler<bool>(_ => true, _ => true, alwaysHandles: true));
    }

    // TODO Confirm use case - This is theoretically a breaking change. Conceptually it should not be because Object handler
    // with no predicate is the same as a ReceiveAny handler.
    [Fact(DisplayName = "Should_Fail_When_AddingAnyReceiveHandler_After_ObjectTypedReceiveHandlerWithNoPredicate")]
    public void Given_ObjectTypedReceiveHandlerWithNoPredicate_When_Adding_AnyReceiveHandler_Then_Should_Fail()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true, alwaysHandles: true);

        // This should throw because the object handler is already added and would catch this before.
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddReceiveAnyHandler(_ => { }));
    }

    // The following tests establish parity with v1.5.71's MatchBuilder-based implementation for the
    // "object handler with no predicate blocks later handlers" rule. In 1.5.71, only an always-handling
    // registration (what Action<T>-based Receive/ReceiveAsync overloads produce) for T/messageType=object
    // with no predicate entered the "no more handlers" state - a Func<T,bool>/Func<object,bool> handler
    // (which may legitimately decline/return false) never did, for either the generic or the typed path.
    // See https://github.com/akkadotnet/akka.net/pull/7557 for the regression this restores.

    [Fact(DisplayName = "Should_Allow_MoreHandlers_When_TypedObjectHandlerWithNoPredicate_DoesNotAlwaysHandle")]
    public void Should_Allow_MoreHandlers_When_TypedObjectHandlerWithNoPredicate_DoesNotAlwaysHandle()
    {
        var handlers = new ReceiveActorHandlers();

        // Mirrors Receive(typeof(object), Func<object,bool>) - the handler may decline (return false),
        // so it must not block later registrations.
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true, alwaysHandles: false);

        handlers.AddTypedReceiveHandler(typeof(string), null, _ => true, alwaysHandles: false);
        handlers.AddGenericReceiveHandler<int>(null, _ => true, alwaysHandles: true);
        handlers.AddReceiveAnyHandler(_ => { });
    }

    [Fact(DisplayName = "Should_Block_MoreHandlers_When_TypedObjectHandlerWithNoPredicate_AlwaysHandles")]
    public void Should_Block_MoreHandlers_When_TypedObjectHandlerWithNoPredicate_AlwaysHandles()
    {
        var handlers = new ReceiveActorHandlers();

        // Mirrors Receive(typeof(object), Action<object>) - the handler always returns true,
        // so later registrations must be rejected, same as a ReceiveAny handler would be.
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true, alwaysHandles: true);

        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(string), null, _ => true, alwaysHandles: false));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddGenericReceiveHandler<int>(null, _ => true, alwaysHandles: true));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddReceiveAnyHandler(_ => { }));
    }

    [Fact(DisplayName = "Should_Allow_MoreHandlers_When_GenericObjectHandlerWithNoPredicate_DoesNotAlwaysHandle")]
    public void Should_Allow_MoreHandlers_When_GenericObjectHandlerWithNoPredicate_DoesNotAlwaysHandle()
    {
        var handlers = new ReceiveActorHandlers();

        // Mirrors Receive<object>(Func<object,bool>) - the handler may decline (return false),
        // so it must not block later registrations.
        handlers.AddGenericReceiveHandler<object>(null, _ => true, alwaysHandles: false);

        handlers.AddTypedReceiveHandler(typeof(string), null, _ => true, alwaysHandles: false);
        handlers.AddGenericReceiveHandler<int>(null, _ => true, alwaysHandles: true);
        handlers.AddReceiveAnyHandler(_ => { });
    }

    [Fact(DisplayName = "Should_Block_MoreHandlers_When_GenericObjectHandlerWithNoPredicate_AlwaysHandles")]
    public void Should_Block_MoreHandlers_When_GenericObjectHandlerWithNoPredicate_AlwaysHandles()
    {
        var handlers = new ReceiveActorHandlers();

        // Mirrors Receive<object>(Action<object>) - the handler always returns true, so later
        // registrations must be rejected. (Prior to this fix, the generic path never set this flag
        // at all, regardless of T or alwaysHandles - a second regression relative to v1.5.71.)
        handlers.AddGenericReceiveHandler<object>(null, _ => true, alwaysHandles: true);

        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(string), null, _ => true, alwaysHandles: false));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddGenericReceiveHandler<int>(null, _ => true, alwaysHandles: true));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddReceiveAnyHandler(_ => { }));
    }

    [Fact(DisplayName = "Should_Succeed_When_AddingSameGenericReceiveHandlerWithPredicate_After_GenericReceiveHandlerWithPredicate")]
    public void Given_GenericReceiveHandlerWithPredicate_When_Adding_SameGenericReceiveHandlerWithPredicate_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddGenericReceiveHandler<int>(_ => true, _ => true, alwaysHandles: true);

        // The handler added has a predicate which makes it uncertain if it will handle the message.
        // Adding another handler for the same type combination should be allowed.
        handlers.AddGenericReceiveHandler<int>(null, _ => true, alwaysHandles: true);
    }

    [Fact(DisplayName = "Should_Succeed_When_AddingDifferentTypedReceiveHandler_After_TypedReceiveHandler")]
    public void Given_TypedReceiveHandler_When_Adding_DifferentTypedReceiveHandler_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(string), _ => true, _ => true, alwaysHandles: true);

        handlers.AddTypedReceiveHandler(typeof(int), _ => true, _ => true, alwaysHandles: true);
    }

    [Fact(DisplayName = "Should_Succeed_When_AddingDifferentGenericReceiveHandler_After_GenericReceiveHandler")]
    public void Given_GenericReceiveHandler_When_Adding_DifferentGenericReceiveHandler_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddGenericReceiveHandler<string>(null, _ => true, alwaysHandles: true);

        handlers.AddGenericReceiveHandler<int>(_ => true, _ => true, alwaysHandles: true);
    }

    [Fact(DisplayName = "Should_Succeed_When_AddingDifferentTypedReceiveHandlerWithPredicate_After_TypedReceiveHandlerWithPredicate")]
    public void Given_TypedReceiveHandlerWithPredicate_When_Adding_DifferentTypedReceiveHandlerWithPredicate_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true, alwaysHandles: true);

        // This should be allowed because the object handler is already but it has a predicate that might not match.
        handlers.AddTypedReceiveHandler(typeof(int), _ => true, _ => true, alwaysHandles: true);
    }

    /*
     * IFoo
     * Bar: IFoo
     *
     * Receive<IFoo>
     * Receive<Bar>
     */

    private interface IFoo { }
    private class Bar : IFoo { }
    private class Baz : IFoo { }

    private static readonly Predicate<IFoo> FooPredicate = _ => true;
    private static readonly Predicate<Baz> BazPredicate = _ => true;

    [Theory(DisplayName = "Should_PreserveMatcherOrdering_When_TypedReceiveHandlerMatchesInterfaceOnConcreteTypes")]
    [InlineData(true)]
    [InlineData(false)]
    public void Given_TypedReceiveHandler_can_match_interface_on_ConcreteTypes(bool usePredicate)
    {
        var handlers1 = new ReceiveActorHandlers();

        var setBaz = false;
        Func<Baz, bool> bazHandler = _ =>
        {
            setBaz = true;
            return true;
        };

        var setInterface = false;
        var interfaceHandler = new Func<IFoo, bool>(_ =>
        {
            setInterface = true;
            return true;
        });

        // ensure that the interface handler is called when a concrete type is passed
        handlers1.AddGenericReceiveHandler(usePredicate ? FooPredicate : null, interfaceHandler, alwaysHandles: true);

        handlers1.TryHandle(new Bar());
        Assert.True(setInterface);

        // now add the Baz handler
        setInterface = false; // reset
        handlers1.AddGenericReceiveHandler(usePredicate ? BazPredicate : null, bazHandler, alwaysHandles: true);

        // demonstrate the matcher ordering is preserved - interface handler should still be called
        handlers1.TryHandle(new Baz());
        Assert.False(setBaz);
        Assert.True(setInterface);

        // reset
        setInterface = false;

        // create a new match handler
        var handlers2 = new ReceiveActorHandlers();

        // set in a "correct" / non-greedy order
        handlers2.AddGenericReceiveHandler(usePredicate ? BazPredicate : null, bazHandler, alwaysHandles: true);
        handlers2.AddGenericReceiveHandler(usePredicate ? FooPredicate : null, interfaceHandler, alwaysHandles: true);

        // demonstrate the matcher ordering is preserved - Baz handler should be called
        handlers2.TryHandle(new Baz());

        Assert.True(setBaz);
        Assert.False(setInterface);

        // reset
        setBaz = false;

        // handle Bar
        handlers2.TryHandle(new Bar());

        // demonstrate the matcher ordering is preserved - interface handler should still be called
        Assert.True(setInterface);
        Assert.False(setBaz); // just a sanity check
    }
}
