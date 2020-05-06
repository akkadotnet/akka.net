//-----------------------------------------------------------------------
// <copyright file="FsApi.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

module Akka.Persistence.FSharp

open System
open Akka.Actor
open Akka.FSharp
open Akka.Persistence

type PersistenceId = string

[<Interface>]
type Snapshotter<'State> =
    abstract LoadSnapshot: PersistenceId -> SnapshotSelectionCriteria -> int64 -> unit
    abstract SaveSnapshot: 'State -> unit
    abstract DeleteSnapshot: int64 -> unit
    abstract DeleteSnapshots: SnapshotSelectionCriteria -> unit

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
    /// Returns a sender of current message or <see cref="ActorRefs.NoSender" />, if none could be determined.
    /// </summary>
    abstract Sender : unit -> IActorRef
    
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
    abstract PersistEvent: ('Event -> 'State) -> 'Event seq -> unit
    
    /// <summary>
    /// Asynchronously persists sequence of events in the event journal. Use second argument 
    /// to define function which will update state depending on events.
    /// </summary>
    abstract AsyncPersistEvent: ('Event -> 'State) -> 'Event seq -> unit

    /// <summary>
    /// Defers a second argument (update state callback) to be called after persisting target
    /// event will be confirmed.
    /// </summary>
    abstract DeferEvent: ('Event -> 'State) -> 'Event seq -> unit
    
    /// <summary>
    /// Defers a callback function to be called after persisting events will be confirmed.
    /// </summary>
    abstract DeferAsync: (obj -> unit) -> obj -> unit

    /// <summary>
    /// Returns currently attached journal actor reference.
    /// </summary>
    abstract Journal: unit -> IActorRef
    
    /// <summary>
    /// Returns currently attached snapshot store actor reference.
    /// </summary>
    abstract SnapshotStore: unit -> IActorRef
    
    /// <summary>
    /// Returns value determining if current persistent view is actually recovering.
    /// </summary>
    abstract IsRecovering: unit -> bool
    
    /// <summary>
    /// Returns last sequence number attached to latest persisted event.
    /// </summary>
    abstract LastSequenceNr: unit -> int64

    /// <summary>
    /// Persistent actor's identifier that doesn't change across different actor incarnations.
    /// </summary>
    abstract PersistenceId: unit -> PersistenceId

type Aggregate<'Command, 'Event, 'State> = {
    state: 'State
    apply: Eventsourced<'Command, 'Event, 'State> -> 'State -> 'Event -> 'State
    exec: Eventsourced<'Command, 'Event, 'State> -> 'State -> 'Command -> unit
}

type FunPersistentActor<'Command, 'Event, 'State>(aggregate: Aggregate<'Command, 'Event, 'State>, name: PersistenceId) as this =
    inherit UntypedPersistentActor()

    let mutable deferables = []
    let mutable state: 'State = aggregate.state
    let mailbox = 
        let self' = this.Self
        let context = UntypedPersistentActor.Context :> IActorContext
        let updateState (updater: 'Event -> 'State) e : unit = 
            state <- updater e
            ()
        { new Eventsourced<'Command, 'Event, 'State> with
            member __.Self = self'
            member __.Context = context
            member __.Sender() = this.Sender()
            member __.Unhandled msg = this.Unhandled msg
            member __.ActorOf(props, name) = context.ActorOf(props, name)
            member __.ActorSelection(path : string) = context.ActorSelection(path)
            member __.ActorSelection(path : ActorPath) = context.ActorSelection(path)
            member __.Watch(aref:IActorRef) = context.Watch aref
            member __.WatchWith(aref:IActorRef, msg) = context.WatchWith (aref, msg)
            member __.Unwatch(aref:IActorRef) = context.Unwatch aref
            member __.Log = lazy (Akka.Event.Logging.GetLogger(context)) 
            member __.Defer fn = deferables <- fn::deferables
            member __.DeferEvent callback events = events |> Seq.iter (fun e -> this.DeferAsync(e, Action<_>(updateState callback)))
            member __.DeferAsync callback obj = this.DeferAsync(obj, Action<_>(callback))
            member __.PersistEvent callback events = this.PersistAll(events, Action<_>(updateState callback))
            member __.AsyncPersistEvent callback events = this.PersistAllAsync(events, Action<_>(updateState callback)) 
            member __.Journal() = this.Journal
            member __.SnapshotStore() = this.SnapshotStore
            member __.PersistenceId() = this.PersistenceId
            member __.IsRecovering() = this.IsRecovering
            member __.LastSequenceNr() = this.LastSequenceNr
            member __.LoadSnapshot pid criteria seqNr = this.LoadSnapshot(pid, criteria, seqNr)
            member __.SaveSnapshot state = this.SaveSnapshot(state)
            member __.DeleteSnapshot seqNr = this.DeleteSnapshot(seqNr)
            member __.DeleteSnapshots criteria = this.DeleteSnapshots(criteria) }
            
    member __.Sender() : IActorRef = base.Sender
    member __.Unhandled msg = base.Unhandled msg
    override x.OnCommand (msg: obj) = 
        match msg with
        | :? 'Command as cmd -> aggregate.exec mailbox state cmd
        | _ -> 
            let serializer = UntypedActor.Context.System.Serialization.FindSerializerForType typeof<'Command>
            match msg with
            | :? (byte[]) as bytes -> 
                let cmd = serializer.FromBinary(bytes, typeof<'Command>) :?> 'Command
                aggregate.exec mailbox state cmd
            | _ -> x.Unhandled msg
    override x.OnRecover (msg: obj) = 
        match msg with
        | :? 'Event as e -> state <- aggregate.apply mailbox state e
        | _ -> 
            let serializer = UntypedActor.Context.System.Serialization.FindSerializerForType typeof<'Event>
            match msg with
            | :? (byte[]) as bytes -> 
                let e = serializer.FromBinary(bytes, typeof<'Event>) :?> 'Event
                state <- aggregate.apply mailbox state e
            | _ -> x.Unhandled msg
    override x.PostStop () =
        base.PostStop ()
        List.iter (fun fn -> fn()) deferables
    default x.PersistenceId = name


[<Interface>]
type View<'Event, 'State> =
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
    /// Returns a sender of current message or <see cref="ActorRefs.NoSender" />, if none could be determined.
    /// </summary>
    abstract Sender : unit -> IActorRef
    
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
    abstract Journal: unit -> IActorRef
    
    /// <summary>
    /// Returns currently attached snapshot store actor reference.
    /// </summary>
    abstract SnapshotStore: unit -> IActorRef
        
    /// <summary>
    /// Returns last sequence number attached to latest persisted event.
    /// </summary>
    abstract LastSequenceNr: unit -> int64

    /// <summary>
    /// Persistent actor's identifier that doesn't change across different actor incarnations.
    /// </summary>
    abstract PersistenceId: unit -> PersistenceId
    
    /// <summary>
    /// Returns value determining if current persistent actor is actually recovering.
    /// </summary>
    abstract IsRecovering: unit -> bool

    /// <summary>
    /// View's identifier that doesn't change across different view incarnations.
    /// </summary>
    abstract ViewId: unit -> PersistenceId

type Perspective<'Event, 'State> = {
    state: 'State
    apply: View<'Event, 'State> -> 'State -> 'Event -> 'State
}

[<Interface>]
type Delivery<'Command, 'Event, 'State> =
    inherit Eventsourced<'Command, 'Event, 'State>
    
    abstract Deliver: (int64 -> obj) -> ActorPath -> unit
    abstract ConfirmDelivery: int64 -> bool
    abstract GetDeliverySnapshot: unit -> AtLeastOnceDeliverySnapshot
    abstract SetDeliverySnapshot: AtLeastOnceDeliverySnapshot -> unit
    abstract UnconfirmedCount: unit -> int

type DeliveryAggregate<'Command, 'Event, 'State> = {
    state: 'State
    apply: Delivery<'Command, 'Event, 'State> -> 'State -> 'Event -> 'State
    exec: Delivery<'Command, 'Event, 'State> -> 'State -> 'Command -> unit
}

type Deliverer<'Command, 'Event, 'State>(aggregate: DeliveryAggregate<'Command, 'Event, 'State>, name: PersistenceId) as this =
    inherit AtLeastOnceDeliveryActor()
    
    let mutable deferables = []
    let mutable state: 'State = aggregate.state
    let mailbox = 
        let self' = this.Self
        let context = AtLeastOnceDeliveryActor.Context :> IActorContext
        let updateState (updater: 'Event -> 'State) e : unit = 
            state <- updater e
            ()
        { new Delivery<'Command, 'Event, 'State> with
            member __.Self = self'
            member __.Context = context
            member __.Sender() = this.Sender()
            member __.Unhandled msg = this.Unhandled msg
            member __.ActorOf(props, name) = context.ActorOf(props, name)
            member __.ActorSelection(path : string) = context.ActorSelection(path)
            member __.ActorSelection(path : ActorPath) = context.ActorSelection(path)
            member __.Watch(aref:IActorRef) = context.Watch aref
            member __.WatchWith(aref:IActorRef, msg) = context.WatchWith (aref, msg)
            member __.Unwatch(aref:IActorRef) = context.Unwatch aref
            member __.Log = lazy (Akka.Event.Logging.GetLogger(context)) 
            member __.Defer fn = deferables <- fn::deferables
            member __.DeferEvent callback events = events |> Seq.iter (fun e -> this.DeferAsync(e, Action<_>(updateState callback)))
            member __.DeferAsync callback obj = this.DeferAsync(obj, Action<_>(callback))
            member __.PersistEvent callback events = this.PersistAll(events, Action<_>(updateState callback))
            member __.AsyncPersistEvent callback events = this.PersistAllAsync(events, Action<_>(updateState callback)) 
            member __.Journal() = this.Journal
            member __.SnapshotStore() = this.SnapshotStore
            member __.PersistenceId() = this.PersistenceId
            member __.IsRecovering() = this.IsRecovering
            member __.LastSequenceNr() = this.LastSequenceNr
            member __.LoadSnapshot pid criteria seqNr = this.LoadSnapshot(pid, criteria, seqNr)
            member __.SaveSnapshot state = this.SaveSnapshot(state)
            member __.DeleteSnapshot seqNr = this.DeleteSnapshot(seqNr)
            member __.DeleteSnapshots criteria = this.DeleteSnapshots(criteria)
            member __.Deliver mapper path = this.Deliver(path, System.Func<_,_>(mapper))
            member __.ConfirmDelivery id = this.ConfirmDelivery(id)
            member __.GetDeliverySnapshot() = this.GetDeliverySnapshot()
            member __.SetDeliverySnapshot snap = this.SetDeliverySnapshot snap
            member __.UnconfirmedCount() = this.UnconfirmedCount }
      
    member __.Sender() : IActorRef = base.Sender
    member __.Unhandled msg = base.Unhandled msg
    override x.ReceiveCommand (msg: obj) = 
        match msg with
        | :? 'Command as cmd -> aggregate.exec mailbox state cmd
        | _ -> 
            match msg with
            | :? (byte[]) as bytes -> 
                let serializer = UntypedActor.Context.System.Serialization.FindSerializerForType typeof<'Command>
                let cmd = serializer.FromBinary(bytes, typeof<'Command>) :?> 'Command
                aggregate.exec mailbox state cmd
            | _ -> x.Unhandled msg
        true
    override x.ReceiveRecover (msg: obj) = 
        match msg with
        | :? 'Event as e -> state <- aggregate.apply mailbox state e
        | _ -> 
            let serializer = UntypedActor.Context.System.Serialization.FindSerializerForType typeof<'Event>
            match msg with
            | :? (byte[]) as bytes -> 
                let e = serializer.FromBinary(bytes, typeof<'Event>) :?> 'Event
                state <- aggregate.apply mailbox state e
            | _ -> x.Unhandled msg
        true
    override x.PostStop () =
        base.PostStop ()
        List.iter (fun fn -> fn()) deferables
    default x.PersistenceId = name

    
module Linq = 
    open System.Linq.Expressions
    open Akka.FSharp.Linq
    
    type PersistentExpression = 

        static member ToExpression(f : System.Linq.Expressions.Expression<System.Func<FunPersistentActor<'Command, 'Event, 'State>>>) = 
            match f with
            | Lambda(_, Invoke(Call(null, Method "ToFSharpFunc", Ar [| Lambda(_, p) |]))) -> 
                Expression.Lambda(p, [||]) :?> System.Linq.Expressions.Expression<System.Func<FunPersistentActor<'Command, 'Event, 'State>>>
            | _ -> failwith "Doesn't match"
            
        static member ToExpression(f : System.Linq.Expressions.Expression<System.Func<Deliverer<'Command, 'Event, 'State>>>) = 
            match f with
            | Lambda(_, Invoke(Call(null, Method "ToFSharpFunc", Ar [| Lambda(_, p) |]))) -> 
                Expression.Lambda(p, [||]) :?> System.Linq.Expressions.Expression<System.Func<Deliverer<'Command, 'Event, 'State>>>
            | _ -> failwith "Doesn't match"
     
/// <summary>
/// Spawns a persistent actor instance.
/// </summary>
/// <param name="actorFactory">Object responsible for actor instantiation.</param>
/// <param name="name">Identifies uniquely current actor across different incarnations. It's necessary to identify it's event source.</param>
/// <param name="aggregate">Aggregate containing state of the actor, but also an event- and command-handling behavior.</param>
/// <param name="options">Additional spawning options.</param>
let spawnPersist (actorFactory : IActorRefFactory) (name : PersistenceId) (aggregate: Aggregate<'Command, 'Event, 'State>) (options : SpawnOption list) : IActorRef =
    let e = Linq.PersistentExpression.ToExpression(fun () -> new FunPersistentActor<'Command, 'Event, 'State>(aggregate, name))
    let props = applySpawnOptions (Props.Create e) options
    actorFactory.ActorOf(props, name)
        
/// <summary>
/// Spawns a guaranteed delivery actor. This actor can deliver messages using at-least-once delivery semantics.
/// </summary>
/// <param name="actorFactory">Object responsible for actor instantiation.</param>
/// <param name="name">Identifies uniquely current actor across different incarnations. It's necessary to identify it's event source.</param>
/// <param name="aggregate">Aggregate containing state of the actor, but also an event- and command-handling behavior.</param>
/// <param name="options">Additional spawning options.</param>
let spawnDeliverer (actorFactory : IActorRefFactory) (name : PersistenceId) (aggregate: DeliveryAggregate<'Command, 'Event, 'State>) (options : SpawnOption list) : IActorRef =
    let e = Linq.PersistentExpression.ToExpression(fun () -> new Deliverer<'Command, 'Event, 'State>(aggregate, name))
    let props = applySpawnOptions (Props.Create e) options
    actorFactory.ActorOf(props, name)

