//-----------------------------------------------------------------------
// <copyright file="PersistentView.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
namespace Akka.Persistence.FSharp

open System
open Akka.Actor
open Akka.FSharp
open Akka.Persistence

[<Interface>]
type View<'Event, 'State> = 
    inherit IActorRefFactory
    inherit ICanWatch
    inherit Snapshotter<'Event>
    
    /// <summary>
    /// Gets <see cref="IActorRef" /> for the current actor.
    /// </summary>
    abstract Self : IActorRef
    
    /// <summary>
    /// Explicitly retrieves next incoming message from the mailbox.
    /// </summary>
    abstract Receive : unit -> IO<'Message>
    
    /// <summary>
    /// Gets the current actor context.
    /// </summary>
    abstract Context : IActorContext
    
    /// <summary>
    /// Returns a sender of current message or <see cref="ActorRefs.NoSender" />, if none could be determined.
    /// </summary>
    abstract Sender<'Response> : unit -> IActorRef<'Response>
    
    /// <summary>
    /// Explicit signalization of unhandled message.
    /// </summary>
    abstract Unhandled : obj -> unit
    
    /// <summary>
    /// Lazy logging adapter. It won't be initialized until logging function will be called. 
    /// </summary>
    abstract Log : Lazy<Akka.Event.ILoggingAdapter>
    
    /// <summary>
    /// Defers a function execution to the moment, when actor is suposed to end it's lifecycle.
    /// Provided function is guaranteed to be invoked no matter of actor stop reason.
    /// </summary>
    abstract Defer : (unit -> unit) -> unit
    
    /// <summary>
    /// Returns currently attached journal actor reference.
    /// </summary>
    abstract Journal : IActorRef
    
    /// <summary>
    /// Returns currently attached snapshot store actor reference.
    /// </summary>
    abstract SnapshotStore : IActorRef
    
    /// <summary>
    /// Returns last sequence number attached to latest persisted event.
    /// </summary>
    abstract LastSequenceNr : unit -> int64
    
    /// <summary>
    /// Persistent actor's identifier that doesn't change across different actor incarnations.
    /// </summary>
    abstract PersistenceId : PersistenceId
    
    /// <summary>
    /// View's identifier that doesn't change across different view incarnations.
    /// </summary>
    abstract ViewId : PersistenceId

type FunPersistentView<'Message, 'State>(actor : View<'Message, 'State> -> Cont<'Message, 'Message>, name : PersistenceId, viewId : PersistenceId) as this = 
    inherit PersistentView()
    let mutable deferables = []
    
    let mutable state = 
        let self' = this.Self
        let context = PersistentView.Context
        actor { new View<'Message, 'State> with
                    member __.Self = self'
                    member __.Receive() = Input
                    member __.Context = context
                    member __.Sender<'Response>() = typed (this.Sender()) :> IActorRef<'Response>
                    member __.Unhandled msg = this.Unhandled msg
                    member __.ActorOf(props, name) = context.ActorOf(props, name)
                    member __.ActorSelection(path : string) = context.ActorSelection(path)
                    member __.ActorSelection(path : ActorPath) = context.ActorSelection(path)
                    member __.Watch(aref : IActorRef) = context.Watch aref
                    member __.Unwatch(aref : IActorRef) = context.Unwatch aref
                    member __.Log = lazy (Akka.Event.Logging.GetLogger(context))
                    member __.Defer fn = deferables <- fn :: deferables
                    member __.Journal = this.Journal
                    member __.SnapshotStore = this.SnapshotStore
                    member __.PersistenceId = this.PersistenceId
                    member __.ViewId = this.ViewId
                    member __.LastSequenceNr() = this.LastSequenceNr
                    member __.LoadSnapshot pid criteria seqNr = this.LoadSnapshot(pid, criteria, seqNr)
                    member __.SaveSnapshot state = this.SaveSnapshot(state)
                    member __.DeleteSnapshot seqNr timestamp = this.DeleteSnapshot(seqNr, timestamp)
                    member __.DeleteSnapshots criteria = this.DeleteSnapshots(criteria) }
    
    member __.Sender() : IActorRef = base.Sender
    member __.Unhandled msg = base.Unhandled msg
    
    override x.Receive msg = 
        match state with
        | Func f -> 
            state <- match msg with
                     | :? 'Message as m -> f m
                     | _ -> 
                         x.Unhandled msg
                         state
        | Return _ -> x.PostStop()
        true
    
    override x.PostStop() = 
        base.PostStop()
        List.iter (fun fn -> fn()) deferables
    
    override x.PersistenceId = name
    override x.ViewId = viewId
