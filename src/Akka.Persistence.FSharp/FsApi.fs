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
    abstract DeleteSnapshot: int64 -> DateTime -> unit
    abstract DeleteSnapshots: SnapshotSelectionCriteria -> unit

[<Interface>]
type Eventsourced<'Command, 'Event, 'State> =
    inherit ActorRefFactory
    inherit ICanWatch
    inherit Snapshotter<'State>
        
    /// <summary>
    /// Gets <see cref="ActorRef" /> for the current actor.
    /// </summary>
    abstract Self : ActorRef
    
    /// <summary>
    /// Gets the current actor context.
    /// </summary>
    abstract Context : IActorContext
    
    /// <summary>
    /// Returns a sender of current message or <see cref="ActorRef.NoSender" />, if none could be determined.
    /// </summary>
    abstract Sender : unit -> ActorRef
    
    /// <summary>
    /// Explicit signalization of unhandled message.
    /// </summary>
    abstract Unhandled : obj -> unit
    
    /// <summary>
    /// Lazy logging adapter. It won't be initialized until logging function will be called. 
    /// </summary>
    abstract Log : Lazy<Akka.Event.LoggingAdapter>

    /// <summary>
    /// Persists sequence of events in the event journal. Use second argument to define 
    /// function which will update state depending on events.
    /// </summary>
    abstract Persist: 'Event seq -> ('Event -> 'State) -> unit
    
    /// <summary>
    /// Asynchronously persists sequence of events in the event journal. Use second argument 
    /// to define function which will update state depending on events.
    /// </summary>
    abstract AsyncPersist: 'Event seq -> ('Event -> 'State) -> unit

    /// <summary>
    /// Defers a second argument (update state callback) to be called after persisting target
    /// event will be confirmed.
    /// </summary>
    abstract Defer: 'Event seq -> ('Event -> 'State) -> unit

    /// <summary>
    /// Returns currently attached journal actor reference.
    /// </summary>
    abstract Journal: unit -> ActorRef
    
    /// <summary>
    /// Returns currently attached snapshot store actor reference.
    /// </summary>
    abstract SnapshotStore: unit -> ActorRef
    
    /// <summary>
    /// Returns value determining if current persistent actor is actually recovering.
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
    exec: Eventsourced<'Command, 'Event, 'State> -> 'State -> 'Command -> 'State
}

type FunPersistentActor<'Command, 'Event, 'State>(aggregate: Aggregate<'Command, 'Event, 'State>, name: PersistenceId) as this =
    inherit UntypedPersistentActor()

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
            member __.Watch(aref:ActorRef) = context.Watch aref
            member __.Unwatch(aref:ActorRef) = context.Unwatch aref
            member __.Log = lazy (Akka.Event.Logging.GetLogger(context)) 
            member __.Persist events callback = this.Persist(events, Action<'Event>(updateState callback))
            member __.AsyncPersist events callback  = this.PersistAsync(events, Action<'Event>(updateState callback)) 
            member __.Defer events callback = this.Defer(events, Action<'Event>(updateState callback))
            member __.Journal() = this.Journal
            member __.SnapshotStore() = this.SnapshotStore
            member __.PersistenceId() = this.PersistenceId
            member __.IsRecovering() = this.IsRecovering
            member __.LastSequenceNr() = this.LastSequenceNr
            member __.LoadSnapshot pid criteria seqNr = this.LoadSnapshot(pid, criteria, seqNr)
            member __.SaveSnapshot state = this.SaveSnapshot(state)
            member __.DeleteSnapshot seqNr timestamp = this.DeleteSnapshot(seqNr, timestamp)
            member __.DeleteSnapshots criteria = this.DeleteSnapshots(criteria) }
      
    member __.Sender() : ActorRef = base.Sender
    member __.Unhandled msg = base.Unhandled msg
    override x.OnCommand (msg: obj) = 
        match msg with
        | :? 'Command as cmd -> state <- aggregate.exec mailbox state cmd
        | _ -> ()   // ignore?
    override x.OnRecover (msg: obj) = 
        match msg with
        | :? 'Event as e -> state <- aggregate.apply mailbox state e
        | _ -> ()   // ignore?        
    default x.PersistenceId = name


[<Interface>]
type View<'Event, 'State> =
    inherit ActorRefFactory
    inherit ICanWatch
    inherit Snapshotter<'State>

    /// <summary>
    /// Gets <see cref="ActorRef" /> for the current actor.
    /// </summary>
    abstract Self : ActorRef
    
    /// <summary>
    /// Gets the current actor context.
    /// </summary>
    abstract Context : IActorContext
    
    /// <summary>
    /// Returns a sender of current message or <see cref="ActorRef.NoSender" />, if none could be determined.
    /// </summary>
    abstract Sender : unit -> ActorRef
    
    /// <summary>
    /// Explicit signalization of unhandled message.
    /// </summary>
    abstract Unhandled : obj -> unit
    
    /// <summary>
    /// Lazy logging adapter. It won't be initialized until logging function will be called. 
    /// </summary>
    abstract Log : Lazy<Akka.Event.LoggingAdapter>
    
    /// <summary>
    /// Returns currently attached journal actor reference.
    /// </summary>
    abstract Journal: unit -> ActorRef
    
    /// <summary>
    /// Returns currently attached snapshot store actor reference.
    /// </summary>
    abstract SnapshotStore: unit -> ActorRef
        
    /// <summary>
    /// Returns last sequence number attached to latest persisted event.
    /// </summary>
    abstract LastSequenceNr: unit -> int64

    /// <summary>
    /// Persistent actor's identifier that doesn't change across different actor incarnations.
    /// </summary>
    abstract PersistenceId: unit -> PersistenceId
    
    /// <summary>
    /// View's identifier that doesn't change across different view incarnations.
    /// </summary>
    abstract ViewId: unit -> PersistenceId

type Perspective<'Event, 'State> = {
    state: 'State
    apply: View<'Event, 'State> -> 'State -> 'Event -> 'State
}

type FunPersistentView<'Event, 'State>(perspective: Perspective<'Event, 'State>, name: PersistenceId, viewId: PersistenceId) as this =
    inherit PersistentView()

    let mutable state: 'State = perspective.state
    let mailbox = 
        let self' = this.Self
        let context = PersistentView.Context :> IActorContext
        let updateState (updater: 'Event -> 'State) e : unit = 
            state <- updater e
            ()
        { new View<'Event, 'State> with
            member __.Self = self'
            member __.Context = context
            member __.Sender() = this.Sender()
            member __.Unhandled msg = this.Unhandled msg
            member __.ActorOf(props, name) = context.ActorOf(props, name)
            member __.ActorSelection(path : string) = context.ActorSelection(path)
            member __.ActorSelection(path : ActorPath) = context.ActorSelection(path)
            member __.Watch(aref:ActorRef) = context.Watch aref
            member __.Unwatch(aref:ActorRef) = context.Unwatch aref
            member __.Log = lazy (Akka.Event.Logging.GetLogger(context)) 
            member __.Journal() = this.Journal
            member __.SnapshotStore() = this.SnapshotStore
            member __.PersistenceId() = this.PersistenceId
            member __.ViewId() = this.ViewId
            member __.LastSequenceNr() = this.LastSequenceNr
            member __.LoadSnapshot pid criteria seqNr = this.LoadSnapshot(pid, criteria, seqNr)
            member __.SaveSnapshot state = this.SaveSnapshot(state)
            member __.DeleteSnapshot seqNr timestamp = this.DeleteSnapshot(seqNr, timestamp)
            member __.DeleteSnapshots criteria = this.DeleteSnapshots(criteria) }
      
    member __.Sender() : ActorRef = base.Sender
    member __.Unhandled msg = base.Unhandled msg
    override x.Receive (msg: obj): bool = 
        match msg with
        | :? 'Event as e -> 
            state <- perspective.apply mailbox state e
            true
        | _ -> false   // ignore?        
    default x.PersistenceId = name
    default x.ViewId = viewId
    
module Linq = 
    open System.Linq.Expressions
    open Akka.FSharp.Linq
    
    type PersistentExpression = 
        static member ToExpression(f : System.Linq.Expressions.Expression<System.Func<FunPersistentActor<'Command, 'Event, 'State>>>) = 
            match f with
            | Lambda(_, Invoke(Call(null, Method "ToFSharpFunc", Ar [| Lambda(_, p) |]))) -> 
                Expression.Lambda(p, [||]) :?> System.Linq.Expressions.Expression<System.Func<FunPersistentActor<'Command, 'Event, 'State>>>
            | _ -> failwith "Doesn't match"
        static member ToExpression(f : System.Linq.Expressions.Expression<System.Func<FunPersistentView<'Event, 'State>>>) = 
            match f with
            | Lambda(_, Invoke(Call(null, Method "ToFSharpFunc", Ar [| Lambda(_, p) |]))) -> 
                Expression.Lambda(p, [||]) :?> System.Linq.Expressions.Expression<System.Func<FunPersistentView<'Event, 'State>>>
            | _ -> failwith "Doesn't match"
     
let spawnPersist (actorFactory : ActorRefFactory) (name : string) (aggregate: Aggregate<'Command, 'Event, 'State>) (options : SpawnOption list) : ActorRef =
    let e = Linq.PersistentExpression.ToExpression(fun () -> new FunPersistentActor<'Command, 'Event, 'State>(aggregate, name))
    let props = applySpawnOptions (Props.Create e) options
    actorFactory.ActorOf(props, name)
    
let spawnView (actorFactory : ActorRefFactory) (viewName: string) (name : string)  (perspective: Perspective<'Event, 'State>) (options : SpawnOption list) : ActorRef =
    let e = Linq.PersistentExpression.ToExpression(fun () -> new FunPersistentView<'Event, 'State>(perspective, name, viewName))
    let props = applySpawnOptions (Props.Create e) options
    actorFactory.ActorOf(props, viewName)
