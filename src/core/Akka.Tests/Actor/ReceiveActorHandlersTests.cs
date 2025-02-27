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
    // Tests to do
    // - Review all tests here as they were AI generated
    // - Rename the tests to be more accurate.
    // - Add tests for the AddGenericReceiveHandler method
    // - Adding for IFoo and then sending a message of Bar : IFoo and it handled
    // - Decide if tests here should cater for adding receive handlers after "built"
    // - See if any of the Test_that_signatures_are_equal and Test_that_signatures_differs tests are applicable


    [Fact]
    public void Given_a_ReceiveAny_handler_has_been_added_When_adding_any_handler_Then_it_fails()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddReceiveAnyHandler(_ => { });

        // A ReceiveAny handler has been added, so adding another ReceiveAny handler should fail
        Assert.Throws<InvalidOperationException>(() => 
            handlers.AddReceiveAnyHandler(_ => { }));
    }

    [Fact]
    public void Given_a_ReceiveAny_handler_has_been_added_When_adding_handler_Then_it_fails()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddReceiveAnyHandler(_ => { });

        // A ReceiveAny handler has been added, so adding a handler for object should fail
        // because ReceiveAny and a receive handler for object are essentially the same
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(object), null, _ => true));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(int), null, _ => true));
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddGenericReceiveHandler<bool>(null, _ => true));
    }

    [Fact]
    public void Given_a_TypedReceive_handler_with_predicate_has_been_added_When_adding_any_handler_Then_it_succeeds()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true);

        // As the object handler has a predicate, adding a ReceiveAny handler should be allowed 
        // as the object handler might not handle all objects.
        handlers.AddReceiveAnyHandler(_ => { });
    }

    [Fact]
    public void Given_a_TypedReceive_handler_has_been_added_When_adding_handler_Then_it_fails()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true);

        // As a handler for the type of object with no predicate is added,
        // adding another handler for the same type combination should fail with an exception
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddTypedReceiveHandler(typeof(object), null, _ => true));
    }

    [Fact]
    public void Given_a_TypedReceive_handler_with_predicate_has_been_added_When_adding_handler_Then_it_succeeds()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true);

        // The handler added has a predicate which makes it uncertain if it will handle the message.
        // Adding another handler for the same type combination should be allowed.
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true);
    }
    
    [Fact]
    public void Given_a_Generic_handler_with_predicate_has_been_added_When_adding_handler_Then_it_succeeds()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddGenericReceiveHandler<int>(_ => true, _ => true);

        // The handler added has a predicate which makes it uncertain if it will handle the message.
        // Adding another handler for the same type combination should be allowed.
        handlers.AddGenericReceiveHandler<int>(null, _ => true);
    }
    
    
    [Fact]
    public void Given_a_Generic_handler_with_predicate_has_been_added_When_adding_handler_Then_it_succeeds1()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddGenericReceiveHandler<int>(null, _ => true);

        // The handler has a handler for the type which has no predicate.
        // Adding another handler for the same type combination should not be allowed.
        Assert.Throws<InvalidOperationException>(() =>
            handlers.AddGenericReceiveHandler<int>(_ => true, _ => true));
    }

    [Fact]
    public void Given_a_TypedReceive_handler_for_different_type_When_adding_handler_Then_it_succeeds()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(string), _ => true, _ => true);

        handlers.AddTypedReceiveHandler(typeof(int), _ => true, _ => true);
    }

    [Fact]
    public void Given_a_Generic_handler_for_different_type_When_adding_handler_Then_it_succeeds()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddGenericReceiveHandler<string>(null, _ => true);

        handlers.AddGenericReceiveHandler<int>( _ => true, _ => true);
    }

    [Fact]
    public void Given_a_TypedReceive_handler_with_no_predicate_has_been_added_When_adding_any_handler_Then_it_succeeds()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), null, _ => true);

        // This should throw because the object handler is already added and would catch this before.
        Assert.Throws<InvalidOperationException>(() => 
            handlers.AddTypedReceiveHandler(typeof(int), _ => true, _ => true));
        Assert.Throws<InvalidOperationException>(() => 
            handlers.AddGenericReceiveHandler<bool>(_ => true, _ => true));
    }

    [Fact]
    public void Given_a_TypedReceive_handler_with_predicate_has_been_added_When_adding_typed_handler_Then_it_succeeds()
    {
        var handlers = new ReceiveActorHandlers();
        handlers.AddTypedReceiveHandler(typeof(object), _ => true, _ => true);
        
        // This should be allowed  because the object handler is already but it has a predicate that might not match.
        handlers.AddTypedReceiveHandler(typeof(int), _ => true, _ => true);
    }
}