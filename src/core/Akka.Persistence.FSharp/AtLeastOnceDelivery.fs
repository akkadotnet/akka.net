//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDelivery.fs" company="Akka.NET Project">
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
type Delivery<'Command, 'Event, 'State> = 
    inherit Eventsourced<'Command, 'Event, 'State>
    abstract Deliver : (int64 -> obj) -> ActorPath -> unit
    abstract ConfirmDelivery : int64 -> bool
    abstract GetDeliverySnapshot : unit -> AtLeastOnceDeliverySnapshot
    abstract SetDeliverySnapshot : AtLeastOnceDeliverySnapshot -> unit
    abstract UnconfirmedCount : unit -> int

type DeliveryAggregate<'Command, 'Event, 'State> = 
    { apply : 'State -> 'Event -> 'State
      exec : Cont<'Command, 'Command> }

type Deliverer<'Command, 'Event, 'State>(actor : Delivery<'Command, 'Event, 'State> -> DeliveryAggregate<'Command, 'Event, 'State>, initState : 'State, name : PersistenceId) as this = 
    inherit AtLeastOnceDeliveryActor()
    let mutable deferables = []
    let mutable state = initState
    
    let aggregate = 
        let self' = this.Self
        let context = AtLeastOnceDeliveryActor.Context
        
        let updateState (updater : 'Event -> 'State) e : unit = 
            state <- updater e
            ()
        actor { new Delivery<'Command, 'Event, 'State> with
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
                    member __.DeleteSnapshots criteria = this.DeleteSnapshots(criteria)
                    member __.Deliver mapper path = this.Deliver(path, System.Func<_, _>(mapper))
                    member __.ConfirmDelivery id = this.ConfirmDelivery(id)
                    member __.GetDeliverySnapshot() = this.GetDeliverySnapshot()
                    member __.SetDeliverySnapshot snap = this.SetDeliverySnapshot snap
                    member __.UnconfirmedCount() = this.UnconfirmedCount }
    
    let mutable behavior = aggregate.exec
    member __.Sender() : IActorRef = base.Sender
    member __.Unhandled msg = base.Unhandled msg
    
    override x.ReceiveCommand(msg : obj) = 
        match behavior with
        | Func f -> 
            behavior <- match msg with
                        | :? 'Command as cmd -> f cmd
                        | _ -> 
                            x.Unhandled msg
                            behavior
        | Return _ -> x.PostStop()
        true
    
    override x.ReceiveRecover(msg : obj) = 
        match msg with
        | :? 'Event as e -> state <- aggregate.apply state e
        | _ -> this.Unhandled() // ignore?      
        true
    
    override x.PostStop() = 
        base.PostStop()
        List.iter (fun fn -> fn()) deferables
    
    override x.PersistenceId = name
