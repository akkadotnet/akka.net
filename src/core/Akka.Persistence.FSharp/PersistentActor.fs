//-----------------------------------------------------------------------
// <copyright file="PersistentActor.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
namespace Akka.Persistence.FSharp

open System
open Akka.Actor
open Akka.FSharp
open Akka.Persistence

type PersistenceId = string

[<Interface>]
type Snapshotter<'State> = 
    abstract LoadSnapshot : PersistenceId -> SnapshotSelectionCriteria -> int64 -> unit
    abstract SaveSnapshot : 'State -> unit
    abstract DeleteSnapshot : int64 -> DateTime -> unit
    abstract DeleteSnapshots : SnapshotSelectionCriteria -> unit

[<Interface>]
type Eventsourced<'Command, 'Event, 'State> = 
    inherit IActorRefFactory
    inherit ICanWatch
    inherit Snapshotter<'State>
    
    /// <summary>
    /// Gets <see cref="IActorRef" /> for the current actor.
    /// </summary>
    abstract Self : IActorRef
    
    /// <summary>
    /// Gets the current actor context.
    /// </summary>
    abstract Context : IActorContext
    
    /// <summary>
    /// Explicitly retrieves next incoming message from the mailbox.
    /// </summary>
    abstract Receive : unit -> IO<'Command>
    
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
    /// Persists sequence of events in the event journal. Use second argument to define 
    /// function which will update state depending on events.
    /// </summary>
    abstract PersistEvent : ('Event -> 'State) -> 'Event seq -> unit
    
    /// <summary>
    /// Asynchronously persists sequence of events in the event journal. Use second argument 
    /// to define function which will update state depending on events.
    /// </summary>
    abstract AsyncPersistEvent : ('Event -> 'State) -> 'Event seq -> unit
    
    /// <summary>
    /// Defers a second argument (update state callback) to be called after persisting target
    /// event will be confirmed.
    /// </summary>
    abstract DeferEvent : ('Event -> 'State) -> 'Event seq -> unit
    
    /// <summary>
    /// Returns currently attached journal actor reference.
    /// </summary>
    abstract Journal : IActorRef
    
    /// <summary>
    /// Returns currently attached snapshot store actor reference.
    /// </summary>
    abstract SnapshotStore : IActorRef
    
    /// <summary>
    /// Returns value determining if current persistent actor is actually recovering.
    /// </summary>
    abstract IsRecovering : unit -> bool
    
    /// <summary>
    /// Returns last sequence number attached to latest persisted event.
    /// </summary>
    abstract LastSequenceNr : unit -> int64
    
    /// <summary>
    /// Persistent actor's identifier that doesn't change across different actor incarnations.
    /// </summary>
    abstract PersistenceId : PersistenceId
    
    /// <summary>
    /// Returns current state of an actor
    /// </summary>
    abstract State : unit -> 'State

type Aggregate<'Command, 'Event, 'State> = 
    { apply : 'State -> 'Event -> 'State
      exec : Cont<'Command, 'Command> }

type FunPersistentActor<'Command, 'Event, 'State>(actor : Eventsourced<'Command, 'Event, 'State> -> Aggregate<'Command, 'Event, 'State>, initState : 'State, name : PersistenceId) as this = 
    inherit UntypedPersistentActor()
    let mutable deferables = []
    let mutable state = initState
    
    let aggregate = 
        let self' = this.Self
        let context = UntypedPersistentActor.Context :> IActorContext
        
        let updateState (updater : 'Event -> 'State) e : unit = 
            state <- updater e
            ()
        actor { new Eventsourced<'Command, 'Event, 'State> with
                    member __.Self = self'
                    member __.Receive() = Input
                    member __.Context = context
                    member __.Sender<'Response>() = typed (this.Sender()) :> IActorRef<'Response>
                    member __.State() = state
                    member __.Unhandled msg = this.Unhandled msg
                    member __.ActorOf(props, name) = context.ActorOf(props, name)
                    member __.ActorSelection(path : string) = context.ActorSelection(path)
                    member __.ActorSelection(path : ActorPath) = context.ActorSelection(path)
                    member __.Watch(aref : IActorRef) = context.Watch aref
                    member __.Unwatch(aref : IActorRef) = context.Unwatch aref
                    member __.Log = lazy (Akka.Event.Logging.GetLogger(context))
                    member __.Defer fn = deferables <- fn :: deferables
                    member __.DeferEvent callback events = this.Defer(events, Action<_>(updateState callback))
                    member __.PersistEvent callback events = this.Persist(events, Action<_>(updateState callback))
                    member __.AsyncPersistEvent callback events = 
                        this.PersistAsync(events, Action<_>(updateState callback))
                    member __.Journal = this.Journal
                    member __.SnapshotStore = this.SnapshotStore
                    member __.PersistenceId = this.PersistenceId
                    member __.IsRecovering() = this.IsRecovering
                    member __.LastSequenceNr() = this.LastSequenceNr
                    member __.LoadSnapshot pid criteria seqNr = this.LoadSnapshot(pid, criteria, seqNr)
                    member __.SaveSnapshot state = this.SaveSnapshot(state)
                    member __.DeleteSnapshot seqNr timestamp = this.DeleteSnapshot(seqNr, timestamp)
                    member __.DeleteSnapshots criteria = this.DeleteSnapshots(criteria) }
    
    let mutable behavior = aggregate.exec
    member __.Sender() : IActorRef = base.Sender
    member __.Unhandled msg = base.Unhandled msg
    
    override x.OnCommand(msg : obj) = 
        match behavior with
        | Func f -> 
            behavior <- match msg with
                        | :? 'Command as cmd -> f cmd
                        | _ -> 
                            x.Unhandled msg
                            behavior
        | Return _ -> x.PostStop()
    
    override x.OnRecover(msg : obj) = 
        match msg with
        | :? 'Event as e -> state <- aggregate.apply state e
        | _ -> this.Unhandled() // ignore?        
    
    override x.PostStop() = 
        base.PostStop()
        List.iter (fun fn -> fn()) deferables
    
    override x.PersistenceId = name
