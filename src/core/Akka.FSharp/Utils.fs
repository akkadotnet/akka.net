//-----------------------------------------------------------------------
// <copyright file="Utils.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
namespace Akka.FSharp

open Akka.Actor
open System

[<AutoOpen>]
module Inbox = 
    /// <summary>
    /// Creates an actor-like object, which could be interrogated from the outside. 
    /// Usually it's used to spy on other actors lifecycle.
    /// Most of the inbox methods works in thread-blocking manner.
    /// </summary>
    let inbox (system : ActorSystem) : Inbox = Inbox.Create system
    
    /// <summary> 
    /// Receives a next message sent to the inbox. This is a blocking operation.
    /// Returns None if timeout occurred or message is incompatible with expected response type.
    /// </summary>
    let receive (timeout : TimeSpan) (i : Inbox) : 'Message option = 
        try 
            Some(i.Receive(timeout) :?> 'Message)
        with _ -> None
    
    /// <summary>
    /// Receives a next message sent to the inbox, which satisfies provided predicate. 
    /// This is a blocking operation. Returns None if timeout occurred or message 
    /// is incompatible with expected response type.
    /// </summary>
    let filterReceive (timeout : TimeSpan) (predicate : 'Message -> bool) (i : Inbox) : 'Message option = 
        try 
            let r = 
                i.ReceiveWhere(Predicate<obj>(fun (o : obj) -> 
                                   match o with
                                   | :? 'Message as message -> predicate message
                                   | _ -> false), timeout)
            Some(r :?> 'Message)
        with _ -> None
    
    /// <summary>
    /// Awaits in async block fora  next message sent to the inbox. 
    /// Returns None if message is incompatible with expected response type.
    /// </summary>
    let asyncReceive (i : Inbox) : Async<'Message option> = 
        async { 
            let! r = i.ReceiveAsync() |> Async.AwaitTask
            return match r with
                   | :? 'Message as message -> Some message
                   | _ -> None
        }

[<AutoOpen>]
module Watchers = 
    /// <summary>
    /// Orders a <paramref name="watcher"/> to monitor an actor targeted by provided <paramref name="subject"/>.
    /// When an actor refered by subject dies, a watcher should receive a <see cref="Terminated"/> message.
    /// </summary>
    let monitor (subject : IActorRef) (watcher : ICanWatch) : IActorRef = watcher.Watch subject
    
    /// <summary>
    /// Orders a <paramref name="watcher"/> to stop monitoring an actor refered by provided <paramref name="subject"/>.
    /// </summary>
    let demonitor (subject : IActorRef) (watcher : ICanWatch) : IActorRef = watcher.Unwatch subject

[<AutoOpen>]
module EventStreaming = 
    /// <summary>
    /// Subscribes an actor reference to target channel of the provided event stream.
    /// </summary>
    let subscribe (ref : IActorRef<'Message>) (eventStream : Akka.Event.EventStream) : bool = 
        eventStream.Subscribe(ref, typeof<'Message>)
    
    /// <summary>
    /// Unubscribes an actor reference from target channel of the provided event stream.
    /// </summary>
    let unsubscribe (ref : IActorRef<'Message>) (eventStream : Akka.Event.EventStream) : bool = 
        eventStream.Unsubscribe(ref, typeof<'Message>)
    
    /// <summary>
    /// Publishes an event on the provided event stream. Event channel is resolved from event's type.
    /// </summary>
    let publish (event : 'Event) (eventStream : Akka.Event.EventStream) : unit = eventStream.Publish event
