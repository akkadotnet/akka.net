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
            handlers.AddTypedReceiveHandler(typeof(object), null, _ => true));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(int), null, _ => true));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddGenericReceiveHandler<bool>(null, _ => true));
    }

    [Fact(DisplayName = "Should_Succeed_When_AddingReceiveAnyHandler_After_TypedReceiveHandlerWithPredicate")]
    public void Given_TypedReceiveHandlerWithPredicate_When_Adding_ReceiveAnyHandler_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true);

        handlers.AddReceiveAnyHandler(_ => { });
    }

    // Typed/generic registrations (ReceiveActorHandlers.AddTypedReceiveHandler/AddGenericReceiveHandler)
    // never block later registrations by themselves, even for object with no predicate - only
    // AddReceiveAnyHandler does. An always-handling object/no-predicate Receive(Action<...>) overload is
    // routed by ReceiveActor/PersistentActor through AddReceiveAnyHandler instead (see the tests above),
    // which is what makes it behave like v1.5.71's MatchBuilder, where that case and ReceiveAny shared the
    // identical "no more handlers" state and error message. See
    // https://github.com/akkadotnet/akka.net/pull/7557 for the regression this restores.
    [Fact(DisplayName = "Should_NeverBlock_MoreHandlers_When_TypedOrGenericHandler_RegistersForObjectWithNoPredicate")]
    public void Should_NeverBlock_MoreHandlers_When_TypedOrGenericHandler_RegistersForObjectWithNoPredicate()
    {
        var handlers = new ReceiveActorHandlers();

        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true);
        handlers.AddGenericReceiveHandler<object>(null, _ => true);
        handlers.AddTypedReceiveHandler(typeof(string), null, _ => true);
        handlers.AddGenericReceiveHandler<int>(null, _ => true);
        handlers.AddReceiveAnyHandler(_ => { });
    }

    [Fact(DisplayName = "Should_Succeed_When_AddingSameTypedReceiveHandlerWithPredicate_After_TypedReceiveHandlerWithPredicate")]
    public void Given_TypedReceiveHandlerWithPredicate_When_Adding_SameTypedReceiveHandlerWithPredicate_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true);

        // The handler added has a predicate which makes it uncertain if it will handle the message.
        // Adding another handler for the same type combination should be allowed.
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true);
    }

    [Fact(DisplayName = "Should_Succeed_When_AddingSameGenericReceiveHandlerWithPredicate_After_GenericReceiveHandlerWithPredicate")]
    public void Given_GenericReceiveHandlerWithPredicate_When_Adding_SameGenericReceiveHandlerWithPredicate_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddGenericReceiveHandler<int>(_ => true, _ => true);

        // The handler added has a predicate which makes it uncertain if it will handle the message.
        // Adding another handler for the same type combination should be allowed.
        handlers.AddGenericReceiveHandler<int>(null, _ => true);
    }

    [Fact(DisplayName = "Should_Succeed_When_AddingDifferentTypedReceiveHandler_After_TypedReceiveHandler")]
    public void Given_TypedReceiveHandler_When_Adding_DifferentTypedReceiveHandler_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(string), _ => true, _ => true);

        handlers.AddTypedReceiveHandler(typeof(int), _ => true, _ => true);
    }

    [Fact(DisplayName = "Should_Succeed_When_AddingDifferentGenericReceiveHandler_After_GenericReceiveHandler")]
    public void Given_GenericReceiveHandler_When_Adding_DifferentGenericReceiveHandler_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddGenericReceiveHandler<string>(null, _ => true);

        handlers.AddGenericReceiveHandler<int>(_ => true, _ => true);
    }

    [Fact(DisplayName = "Should_Succeed_When_AddingDifferentTypedReceiveHandlerWithPredicate_After_TypedReceiveHandlerWithPredicate")]
    public void Given_TypedReceiveHandlerWithPredicate_When_Adding_DifferentTypedReceiveHandlerWithPredicate_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true);

        // This should be allowed because the object handler is already but it has a predicate that might not match.
        handlers.AddTypedReceiveHandler(typeof(int), _ => true, _ => true);
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
        handlers1.AddGenericReceiveHandler(usePredicate ? FooPredicate : null, interfaceHandler);

        handlers1.TryHandle(new Bar());
        Assert.True(setInterface);

        // now add the Baz handler
        setInterface = false; // reset
        handlers1.AddGenericReceiveHandler(usePredicate ? BazPredicate : null, bazHandler);

        // demonstrate the matcher ordering is preserved - interface handler should still be called
        handlers1.TryHandle(new Baz());
        Assert.False(setBaz);
        Assert.True(setInterface);

        // reset
        setInterface = false;

        // create a new match handler
        var handlers2 = new ReceiveActorHandlers();

        // set in a "correct" / non-greedy order
        handlers2.AddGenericReceiveHandler(usePredicate ? BazPredicate : null, bazHandler);
        handlers2.AddGenericReceiveHandler(usePredicate ? FooPredicate : null, interfaceHandler);

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
