//-----------------------------------------------------------------------
// <copyright file="Actors.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
[<AutoOpen>]
module Akka.FSharp.Actors

open Akka.Actor
open Akka.Util
open System
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Linq.QuotationEvaluation
    
/// <summary>
/// Typed version of <see cref="ICanTell"/> interface. Allows to tell/ask using only messages of restricted type.
/// </summary>
[<Interface>]
type ICanTell<'Message> = 
    inherit ICanTell
    abstract Tell : 'Message * IActorRef -> unit
    abstract Ask : 'Message * TimeSpan option -> Async<'Response>

/// <summary>
/// Typed version of <see cref="IActorRef"/> interface. Allows to tell/ask using only messages of restricted type.
/// </summary>
[<Interface>]
type IActorRef<'Message> = 
    inherit ICanTell<'Message>
    inherit IActorRef
    /// <summary>
    /// Changes the type of handled messages, returning new typed ActorRef.
    /// </summary>
    abstract Switch<'T> : unit -> IActorRef<'T>

/// <summary>
/// Wrapper around untyped instance of IActorRef interface.
/// </summary>
type TypedActorRef<'Message>(underlyingRef : IActorRef) = 
    
    /// <summary>
    /// Gets an underlying actor reference wrapped by current object.
    /// </summary>
    member __.Underlying = underlyingRef
    
    interface ICanTell with
        member __.Tell(message : obj, sender : IActorRef) = underlyingRef.Tell(message, sender)
    
    interface IActorRef<'Message> with
        
        /// <summary>
        /// Changes the type of handled messages, returning new typed ActorRef.
        /// </summary>
        member __.Switch<'T>() = TypedActorRef<'T>(underlyingRef) :> IActorRef<'T>
        
        member __.Tell(message : 'Message, sender : IActorRef) = underlyingRef.Tell(message :> obj, sender)
        member __.Ask(message : 'Message, timeout : TimeSpan option) : Async<'Response> = 
            Async.AwaitTask(underlyingRef.Ask<'Response>(message, OptionHelper.toNullable timeout))
        member __.Path = underlyingRef.Path
        
        member __.Equals other = 
            match other with
            | :? TypedActorRef<'Message> as typed -> underlyingRef.Equals(typed.Underlying)
            | _ -> underlyingRef.Equals other
        
        member __.CompareTo other = 
            match other with
            | :? TypedActorRef<'Message> as typed -> underlyingRef.CompareTo(typed.Underlying)
            | _ -> underlyingRef.CompareTo(other)
    
    interface ISurrogated with
        member this.ToSurrogate system = 
            let surrogate : TypedActorRefSurrogate<'Message> = { Wrapped = underlyingRef.ToSurrogate system }
            surrogate :> ISurrogate

and TypedActorRefSurrogate<'Message> = 
    { Wrapped : ISurrogate }
    interface ISurrogate with
        member this.FromSurrogate system = 
            let tref = TypedActorRef<'Message>((this.Wrapped.FromSurrogate system) :?> IActorRef)
            tref :> ISurrogated

/// <summary>
/// Returns typed wrapper over provided actor reference.
/// </summary>
let inline typed (actorRef : IActorRef) : IActorRef<'Message> = 
    (TypedActorRef<'Message> actorRef) :> IActorRef<'Message>

/// <summary>
/// Typed wrapper for <see cref="ActorSelection"/> objects.
/// </summary>
type TypedActorSelection<'Message>(selection : ActorSelection) = 

    /// <summary>
    /// Returns an underlying untyped <see cref="ActorSelection"/> instance.
    /// </summary>
    member __.Underlying = selection

    /// <summary>
    /// Gets and actor ref anchor for current selection.
    /// </summary>
    member __.Anchor with get (): IActorRef<'Message> = typed selection.Anchor

    /// <summary>
    /// Gets string representation for all elements in actor selection path.
    /// </summary>
    member __.PathString with get () = selection.PathString

    /// <summary>
    /// Gets collection of elements, actor selection path is build from.
    /// </summary>
    member __.Path with get () = selection.Path

    /// <summary>
    /// Sets collection of elements, actor selection path is build from.
    /// </summary>
    member __.Path with set (e) = selection.Path <- e

    /// <summary>
    /// Tries to resolve an actor reference from current actor selection.
    /// </summary>
    member __.ResolveOne (timeout: TimeSpan): Async<IActorRef<'Message>> = 
        let convertToTyped (t: System.Threading.Tasks.Task<IActorRef>) = typed t.Result
        selection.ResolveOne(timeout).ContinueWith(convertToTyped)
        |> Async.AwaitTask

    override x.Equals (o:obj) = 
        if obj.ReferenceEquals(x, o) then true
        else match o with
        | :? TypedActorSelection<'Message> as t -> x.Underlying.Equals t.Underlying
        | _ -> x.Underlying.Equals o

    override __.GetHashCode () = selection.GetHashCode() ^^^ typeof<'Message>.GetHashCode() 

    interface ICanTell with
        member __.Tell(message : obj, sender : IActorRef) = selection.Tell(message, sender)
    
    interface ICanTell<'Message> with
        member __.Tell(message : 'Message, sender : IActorRef) : unit = selection.Tell(message, sender)
        member __.Ask(message : 'Message, timeout : TimeSpan option) : Async<'Response> = 
            Async.AwaitTask(selection.Ask<'Response>(message, OptionHelper.toNullable timeout))    

/// <summary>
/// Unidirectional send operator. 
/// Sends a message object directly to actor tracked by actorRef. 
/// </summary>
let inline (<!) (actorRef : #ICanTell<'Message>) (msg : 'Message) : unit = 
    actorRef.Tell(msg, ActorCell.GetCurrentSelfOrNoSender())

/// <summary> 
/// Bidirectional send operator. Sends a message object directly to actor 
/// tracked by actorRef and awaits for response send back from corresponding actor. 
/// </summary>
let inline (<?) (tell : #ICanTell<'Message>) (msg : 'Message) : Async<'Response> = tell.Ask<'Response>(msg, None)

/// Pipes an output of asynchronous expression directly to the recipients mailbox.
let pipeTo (computation : Async<'Message>) (recipient : ICanTell<'Message>) (sender : IActorRef) : unit = 
    let success (result : 'Message) : unit = recipient.Tell(result, sender)
    let failure (err : exn) : unit = recipient.Tell(Status.Failure(err), sender)
    Async.StartWithContinuations(computation, success, failure, failure)

/// Pipe operator which sends an output of asynchronous expression directly to the recipients mailbox.
let inline (|!>) (computation : Async<'Message>) (recipient : ICanTell<'Message>) = 
    pipeTo computation recipient ActorRefs.NoSender

/// Pipe operator which sends an output of asynchronous expression directly to the recipients mailbox
let inline (<!|) (recipient : ICanTell<'Message>) (computation : Async<'Message>) = 
    pipeTo computation recipient ActorRefs.NoSender

type IO<'T> = 
    | Input

/// <summary>
/// Exposes an Akka.NET actor APi accessible from inside of F# continuations -> <see cref="Cont{'In, 'Out}" />
/// </summary>
[<Interface>]
type Actor<'Message> = 
    inherit IActorRefFactory
    inherit ICanWatch
    
    /// <summary>
    /// Explicitly retrieves next incoming message from the mailbox.
    /// </summary>
    abstract Receive : unit -> IO<'Message>
    
    /// <summary>
    /// Gets <see cref="IActorRef" /> for the current actor.
    /// </summary>
    abstract Self : IActorRef<'Message>
    
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
    abstract Unhandled : 'Message -> unit
    
    /// <summary>
    /// Lazy logging adapter. It won't be initialized until logging function will be called. 
    /// </summary>
    abstract Log : Lazy<Akka.Event.ILoggingAdapter>
    
    /// <summary>
    /// Defers provided function to be invoked when actor stops, regardless of reasons.
    /// </summary>
    abstract Defer : (unit -> unit) -> unit
    
    /// <summary>
    /// Stashes the current message (the message that the actor received last)
    /// </summary>
    abstract Stash : unit -> unit
    
    /// <summary>
    /// Unstash the oldest message in the stash and prepends it to the actor's mailbox.
    /// The message is removed from the stash.
    /// </summary>
    abstract Unstash : unit -> unit
    
    /// <summary>
    /// Unstashes all messages by prepending them to the actor's mailbox.
    /// The stash is guaranteed to be empty afterwards.
    /// </summary>
    abstract UnstashAll : unit -> unit

[<AbstractClass>]
type Actor() = 
    inherit UntypedActor()
    interface IWithUnboundedStash with
        member val Stash = null with get, set

/// <summary>
/// Returns an instance of <see cref="ActorSelection" /> for specified path. 
/// If no matching receiver will be found, a <see cref="ActorRefs.NoSender" /> instance will be returned. 
/// </summary>
let inline select (path : string) (selector : IActorRefFactory) : TypedActorSelection<'Message> = 
    TypedActorSelection(selector.ActorSelection path)

/// Gives access to the next message throu let! binding in actor computation expression.
type Cont<'In, 'Out> = 
    | Func of ('In -> Cont<'In, 'Out>)
    | Return of 'Out

/// The builder for actor computation expression.
type ActorBuilder() = 
    
    /// Binds the next message.
    member __.Bind(m : IO<'In>, f : 'In -> _) = Func(fun m -> f m)
    
    /// Binds the result of another actor computation expression.
    member this.Bind(x : Cont<'In, 'Out1>, f : 'Out1 -> Cont<'In, 'Out2>) : Cont<'In, 'Out2> = 
        match x with
        | Func fx -> Func(fun m -> this.Bind(fx m, f))
        | Return v -> f v
    
    member __.ReturnFrom(x) = x
    member __.Return x = Return x
    member __.Zero() = Return()
    
    member this.TryWith(f : unit -> Cont<'In, 'Out>, c : exn -> Cont<'In, 'Out>) : Cont<'In, 'Out> = 
        try 
            true, f()
        with ex -> false, c ex
        |> function 
        | true, Func fn -> Func(fun m -> this.TryWith((fun () -> fn m), c))
        | _, v -> v
    
    member this.TryFinally(f : unit -> Cont<'In, 'Out>, fnl : unit -> unit) : Cont<'In, 'Out> = 
        try 
            match f() with
            | Func fn -> Func(fun m -> this.TryFinally((fun () -> fn m), fnl))
            | r -> 
                fnl()
                r
        with ex -> 
            fnl()
            reraise()
    
    member this.Using(d : #IDisposable, f : _ -> Cont<'In, 'Out>) : Cont<'In, 'Out> = 
        this.TryFinally((fun () -> f d), 
                        fun () -> 
                            if d <> null then d.Dispose())
    
    member this.While(condition : unit -> bool, f : unit -> Cont<'In, unit>) : Cont<'In, unit> = 
        if condition() then 
            match f() with
            | Func fn -> 
                Func(fun m -> 
                    fn m |> ignore
                    this.While(condition, f))
            | v -> this.While(condition, f)
        else Return()
    
    member __.For(source : 'Iter seq, f : 'Iter -> Cont<'In, unit>) : Cont<'In, unit> = 
        use e = source.GetEnumerator()
        
        let rec loop() = 
            if e.MoveNext() then 
                match f e.Current with
                | Func fn -> 
                    Func(fun m -> 
                        fn m |> ignore
                        loop())
                | r -> loop()
            else Return()
        loop()
    
    member __.Delay(f : unit -> Cont<_, _>) = f
    member __.Run(f : unit -> Cont<_, _>) = f()
    member __.Run(f : Cont<_, _>) = f
    
    member this.Combine(f : unit -> Cont<'In, _>, g : unit -> Cont<'In, 'Out>) : Cont<'In, 'Out> = 
        match f() with
        | Func fx -> Func(fun m -> this.Combine((fun () -> fx m), g))
        | Return _ -> g()
    
    member this.Combine(f : Cont<'In, _>, g : unit -> Cont<'In, 'Out>) : Cont<'In, 'Out> = 
        match f with
        | Func fx -> Func(fun m -> this.Combine(fx m, g))
        | Return _ -> g()
    
    member this.Combine(f : unit -> Cont<'In, _>, g : Cont<'In, 'Out>) : Cont<'In, 'Out> = 
        match f() with
        | Func fx -> Func(fun m -> this.Combine((fun () -> fx m), g))
        | Return _ -> g
    
    member this.Combine(f : Cont<'In, _>, g : Cont<'In, 'Out>) : Cont<'In, 'Out> = 
        match f with
        | Func fx -> Func(fun m -> this.Combine(fx m, g))
        | Return _ -> g

type FunActor<'Message, 'Returned>(actor : Actor<'Message> -> Cont<'Message, 'Returned>) as this = 
    inherit Actor()
    let mutable deferables = []
    
    let mutable state = 
        let self' = this.Self
        let context = UntypedActor.Context :> IActorContext
        actor { new Actor<'Message> with
                    member __.Receive() = Input
                    member __.Self = typed self'
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
                    member __.Stash() = (this :> IWithUnboundedStash).Stash.Stash()
                    member __.Unstash() = (this :> IWithUnboundedStash).Stash.Unstash()
                    member __.UnstashAll() = (this :> IWithUnboundedStash).Stash.UnstashAll() }
    
    new(actor : Expr<Actor<'Message> -> Cont<'Message, 'Returned>>) = FunActor(actor.Compile () ())
    member __.Sender() : IActorRef = base.Sender
    member __.Unhandled msg = base.Unhandled msg
    
    override x.OnReceive msg = 
        match state with
        | Func f -> 
            match msg with
            | :? 'Message as m -> state <- f m
            | _ -> x.Unhandled msg
        | Return _ -> x.PostStop()
    
    override x.PostStop() = 
        base.PostStop()
        List.iter (fun fn -> fn()) deferables

/// Builds an actor message handler using an actor expression syntax.
let actor = ActorBuilder()
