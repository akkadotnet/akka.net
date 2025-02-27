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
    [Fact]
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

    [Fact]
    public void Given_TypedReceiveHandlerWithPredicate_When_Adding_ReceiveAnyHandler_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true);

        // As the object handler has a predicate, adding a ReceiveAny handler should be allowed
        // as the object handler might not handle all objects.
        handlers.AddReceiveAnyHandler(_ => { });
    }

    [Fact]
    public void Given_TypedReceiveHandler_When_Adding_SameTypedReceiveHandler_Then_Should_Fail()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true);

        // As a handler for the type of object with no predicate is added,
        // adding another handler for the same type combination should fail with an exception
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(object), null, _ => true));
    }

    [Fact]
    public void Given_TypedReceiveHandlerWithPredicate_When_Adding_SameTypedReceiveHandlerWithPredicate_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true);

        // The handler added has a predicate which makes it uncertain if it will handle the message.
        // Adding another handler for the same type combination should be allowed.
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true);
    }

    [Fact]
    public void Given_ObjectTypedReceiveHandlerWithNoPredicate_When_Adding_Any_Other_ReceiveHandler_Then_Should_Fail()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true);

        // This should throw because the object handler is already added and would catch this before.
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(int), _ => true, _ => true));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddGenericReceiveHandler<bool>(_ => true, _ => true));
    }

    // TODO Confirm use case - This is theoretically a breaking change. Conceptually it should not be because Object handler
    // with no predicate is the same as a ReceiveAny handler.
    [Fact]
    public void Given_ObjectTypedReceiveHandlerWithNoPredicate_When_Adding_AnyReceiveHandler_Then_Should_Fail()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true);

        // This should throw because the object handler is already added and would catch this before.
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddReceiveAnyHandler(_ => { }));
    }

    [Fact]
    public void Given_GenericReceiveHandlerWithPredicate_When_Adding_SameGenericReceiveHandlerWithPredicate_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddGenericReceiveHandler<int>(_ => true, _ => true);

        // The handler added has a predicate which makes it uncertain if it will handle the message.
        // Adding another handler for the same type combination should be allowed.
        handlers.AddGenericReceiveHandler<int>(null, _ => true);
    }

    [Fact]
    public void Given_TypedReceiveHandler_When_Adding_DifferentTypedReceiveHandler_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(string), _ => true, _ => true);

        handlers.AddTypedReceiveHandler(typeof(int), _ => true, _ => true);
    }

    [Fact]
    public void Given_GenericReceiveHandler_When_Adding_DifferentGenericReceiveHandler_Then_Should_Succeed()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddGenericReceiveHandler<string>(null, _ => true);

        handlers.AddGenericReceiveHandler<int>(_ => true, _ => true);
    }

    [Fact]
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
    
}