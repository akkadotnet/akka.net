//-----------------------------------------------------------------------
// <copyright file="FsApi.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.FSharp

open Akka.Actor
open System
open Microsoft.FSharp.Quotations
open FSharp.Quotations.Evaluator

module Serialization = 
    open MBrace.FsPickler
    open Akka.Serialization

    let internal serializeToBinary (fsp:BinarySerializer) o = 
            use stream = new System.IO.MemoryStream()
            fsp.Serialize(stream, o)
            stream.ToArray()

    let internal deserializeFromBinary<'t> (fsp:BinarySerializer) (bytes: byte array) =
            use stream = new System.IO.MemoryStream(bytes)
            fsp.Deserialize<'t> stream
    
    // used for top level serialization
    type ExprSerializer(system) = 
        inherit Serializer(system)
        let fsp = FsPickler.CreateBinarySerializer()
        override __.Identifier = 99
        override __.IncludeManifest = true        
        override __.ToBinary(o) = serializeToBinary fsp o        
        override __.FromBinary(bytes, _) = deserializeFromBinary fsp bytes
        
                        
    let internal exprSerializationSupport (system: ActorSystem) =
        let serializer = ExprSerializer(system :?> ExtendedActorSystem)
        system.Serialization.AddSerializer("Expr serializer", serializer)
        system.Serialization.AddSerializationMap(typeof<Expr>, serializer)

[<AutoOpen>]
module Actors =
    open System.Threading.Tasks

    let private tryCast (t:Task<obj>) : 'Message =
        match t.Result with
        | :? 'Message as m -> m
        | o ->
            let context = Akka.Actor.Internal.InternalCurrentActorCellKeeper.Current
            if context = null 
            then failwith "Cannot cast object outside the actor system context "
            else
                match o with
                | :? (byte[]) as bytes -> 
                    let serializer = context.System.Serialization.FindSerializerForType typeof<'Message>
                    serializer.FromBinary(bytes, typeof<'Message>) :?> 'Message
                | _ -> raise (InvalidCastException("Tried to cast object to " + typeof<'Message>.ToString()))

    /// <summary>
    /// Unidirectional send operator. 
    /// Sends a message object directly to actor tracked by actorRef. 
    /// </summary>
    let inline (<!) (actorRef : #ICanTell) (msg : obj) : unit = actorRef.Tell(msg, ActorCell.GetCurrentSelfOrNoSender())

    /// <summary> 
    /// Bidirectional send operator. Sends a message object directly to actor 
    /// tracked by actorRef and awaits for response send back from corresponding actor. 
    /// </summary>
    let (<?) (tell : #ICanTell) (msg : obj) : Async<'Message> = 
        tell.Ask(msg).ContinueWith(Func<_,'Message>(tryCast), TaskContinuationOptions.ExecuteSynchronously)
        |> Async.AwaitTask

    /// Pipes an output of asynchronous expression directly to the recipients mailbox.
    let pipeTo (computation : Async<'T>) (recipient : ICanTell) (sender : IActorRef) : unit = 
        let success (result : 'T) : unit = recipient.Tell(result, sender)
        let failure (err : exn) : unit = recipient.Tell(Status.Failure(err), sender)
        Async.StartWithContinuations(computation, success, failure, failure)

    /// Pipe operator which sends an output of asynchronous expression directly to the recipients mailbox.
    let inline (|!>) (computation : Async<'T>) (recipient : ICanTell) = pipeTo computation recipient ActorRefs.NoSender

    /// Pipe operator which sends an output of asynchronous expression directly to the recipients mailbox
    let inline (<!|) (recipient : ICanTell) (computation : Async<'T>) = pipeTo computation recipient ActorRefs.NoSender
    
    type ICanTell with
        
        /// <summary>
        /// Sends a message to an actor and asynchronously awaits for a response back until timeout occur.
        /// </summary>
        member this.Ask(msg: obj, timeout: TimeSpan): Async<'Response> =
            this.Ask<'Response>(msg, Nullable(timeout)) |> Async.AwaitTask

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
    let inline select (path : string) (selector : IActorRefFactory) : ActorSelection = selector.ActorSelection path
        
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
                        member __.Stash() = (this :> IWithUnboundedStash).Stash.Stash()
                        member __.Unstash() = (this :> IWithUnboundedStash).Stash.Unstash()
                        member __.UnstashAll() = (this :> IWithUnboundedStash).Stash.UnstashAll() }
        
        new(actor : Expr<Actor<'Message> -> Cont<'Message, 'Returned>>) = FunActor(QuotationEvaluator.Evaluate actor)
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
            base.PostStop ()
            List.iter (fun fn -> fn()) deferables
            

    /// Builds an actor message handler using an actor expression syntax.
    let actor = ActorBuilder()    
    
[<AutoOpen>]
module Logging = 
    open Akka.Event

    /// Logs a message using configured Akka logger.
    let log (level : LogLevel) (mailbox : Actor<'Message>) (msg : string) : unit = 
        let logger = mailbox.Log.Force()
        logger.Log(level, msg)
    
    /// Logs a message at Debug level using configured Akka logger.
    let inline logDebug mailbox msg = log LogLevel.DebugLevel mailbox msg
    
    /// Logs a message at Info level using configured Akka logger.
    let inline logInfo mailbox msg = log LogLevel.InfoLevel mailbox msg
    
    /// Logs a message at Warning level using configured Akka logger.
    let inline logWarning mailbox msg = log LogLevel.WarningLevel mailbox msg
    
    /// Logs a message at Error level using configured Akka logger. 
    let inline logError mailbox msg = log LogLevel.ErrorLevel mailbox msg
    
    /// Logs an exception message at Error level using configured Akka logger.
    let inline logException mailbox (e : exn) = log LogLevel.ErrorLevel mailbox (e.Message)

    open Printf
    
    let inline private doLogf level (mailbox: Actor<'Message>) msg = 
        mailbox.Log.Value.Log(level, msg) |> ignore

    /// Logs a message using configured Akka logger.
    let inline logf (level : LogLevel) (mailbox : Actor<'Message>) = 
        kprintf (doLogf level mailbox)
     
    /// Logs a message at Debug level using configured Akka logger.
    let inline logDebugf mailbox = kprintf (doLogf LogLevel.DebugLevel mailbox)
    
    /// Logs a message at Info level using configured Akka logger.
    let inline logInfof mailbox = kprintf (doLogf LogLevel.InfoLevel mailbox)
    
    /// Logs a message at Warning level using configured Akka logger.
    let inline logWarningf mailbox = kprintf (doLogf LogLevel.WarningLevel mailbox)
    
    /// Logs a message at Error level using configured Akka logger. 
    let inline logErrorf mailbox = kprintf (doLogf LogLevel.ErrorLevel mailbox)


module Linq = 
    open System.Linq.Expressions
    open Microsoft.FSharp.Linq
    
    let (|Lambda|_|) (e : Expression) = 
        match e with
        | :? LambdaExpression as l -> Some(l.Parameters, l.Body)
        | _ -> None
    
    let (|Call|_|) (e : Expression) = 
        match e with
        | :? MethodCallExpression as c -> Some(c.Object, c.Method, c.Arguments)
        | _ -> None
    
    let (|Method|) (e : System.Reflection.MethodInfo) = e.Name
    
    let (|Invoke|_|) = 
        function 
        | Call(o, Method("Invoke"), _) -> Some o
        | _ -> None
    
    let (|Ar|) (p : System.Collections.ObjectModel.ReadOnlyCollection<Expression>) = Array.ofSeq p

    let toExpression<'Actor>(f : System.Linq.Expressions.Expression) = 
            match f with
            | Lambda(_, (Call(null, Method "ToFSharpFunc", Ar [| Lambda(_, p) |]))) 
            | Call(null, Method "ToFSharpFunc", Ar [| Lambda(_, p) |]) -> 
                Expression.Lambda(p, [||]) :?> System.Linq.Expressions.Expression<System.Func<'Actor>>
            | _ -> failwith "Doesn't match"
 
    type Expression = 
        static member ToExpression(f : System.Linq.Expressions.Expression<System.Func<FunActor<'Message, 'v>>>) = f
        static member ToExpression<'Actor>(f : Quotations.Expr<(unit -> 'Actor)>) = 
            toExpression<'Actor> (QuotationEvaluator.ToLinqExpression f)  
        
[<RequireQualifiedAccess>]
module Configuration = 

    /// Parses provided HOCON string into a valid Akka configuration object.
    let parse = Akka.Configuration.ConfigurationFactory.ParseString

    /// Returns default Akka configuration.
    let defaultConfig = Akka.Configuration.ConfigurationFactory.Default

    /// Loads Akka configuration from the project's .config file.
    let load = Akka.Configuration.ConfigurationFactory.Load
    
module internal OptionHelper =
    
    let optToNullable = function
        | Some x -> Nullable x
        | None -> Nullable()
        
open Akka.Util
type ExprDeciderSurrogate(serializedExpr: byte array) =
    member __.SerializedExpr = serializedExpr
    interface ISurrogate with
        member this.FromSurrogate _ = 
            let fsp = MBrace.FsPickler.FsPickler.CreateBinarySerializer()
            let expr = (Serialization.deserializeFromBinary<Expr<(exn->Directive)>> fsp (this.SerializedExpr))
            ExprDecider(expr) :> ISurrogated

and ExprDecider (expr: Expr<(exn->Directive)>) =
    member __.Expr = expr
    member private this.Compiled = lazy (QuotationEvaluator.Evaluate this.Expr)
    interface IDecider with
        member this.Decide (e: exn): Directive = this.Compiled.Value (e)
    interface ISurrogated with
        member this.ToSurrogate _ = 
            let fsp = MBrace.FsPickler.FsPickler.CreateBinarySerializer()        
            ExprDeciderSurrogate(Serialization.serializeToBinary fsp this.Expr) :> ISurrogate
        
type Strategy = 

    /// <summary>
    /// Returns a supervisor strategy appliable only to child actor which faulted during execution.
    /// </summary>
    /// <param name="decider">Used to determine a actor behavior response depending on exception occurred.</param>
    static member OneForOne (decider : exn -> Directive) : SupervisorStrategy = 
        upcast OneForOneStrategy(System.Func<_, _>(decider))
   
    /// <summary>
    /// Returns a supervisor strategy appliable only to child actor which faulted during execution.
    /// </summary>
    /// <param name="retries">Defines a number of times, an actor could be restarted. If it's a negative value, there is not limit.</param>
    /// <param name="timeout">Defines time window for number of retries to occur.</param>
    /// <param name="decider">Used to determine a actor behavior response depending on exception occurred.</param>
    static member OneForOne (decider : exn -> Directive, ?retries : int, ?timeout : TimeSpan)  : SupervisorStrategy = 
        upcast OneForOneStrategy(OptionHelper.optToNullable retries, OptionHelper.optToNullable timeout, System.Func<_, _>(decider))
        
    /// <summary>
    /// Returns a supervisor strategy appliable only to child actor which faulted during execution.
    /// </summary>
    /// <param name="retries">Defines a number of times, an actor could be restarted. If it's a negative value, there is not limit.</param>
    /// <param name="timeout">Defines time window for number of retries to occur.</param>
    /// <param name="decider">Used to determine a actor behavior response depending on exception occurred.</param>
    static member OneForOne (decider : Expr<(exn -> Directive)>, ?retries : int, ?timeout : TimeSpan)  : SupervisorStrategy = 
        upcast OneForOneStrategy(OptionHelper.optToNullable retries, OptionHelper.optToNullable timeout, ExprDecider decider)
    
    /// <summary>
    /// Returns a supervisor strategy appliable to each supervised actor when any of them had faulted during execution.
    /// </summary>
    /// <param name="decider">Used to determine a actor behavior response depending on exception occurred.</param>
    static member AllForOne (decider : exn -> Directive) : SupervisorStrategy = 
        upcast AllForOneStrategy(System.Func<_, _>(decider))
    
    /// <summary>
    /// Returns a supervisor strategy appliable to each supervised actor when any of them had faulted during execution.
    /// </summary>
    /// <param name="retries">Defines a number of times, an actor could be restarted. If it's a negative value, there is not limit.</param>
    /// <param name="timeout">Defines time window for number of retries to occur.</param>
    /// <param name="decider">Used to determine a actor behavior response depending on exception occurred.</param>
    static member AllForOne (decider : exn -> Directive, ?retries : int, ?timeout : TimeSpan) : SupervisorStrategy = 
        upcast AllForOneStrategy(OptionHelper.optToNullable retries, OptionHelper.optToNullable timeout, System.Func<_, _>(decider))
        
    /// <summary>
    /// Returns a supervisor strategy appliable to each supervised actor when any of them had faulted during execution.
    /// </summary>
    /// <param name="retries">Defines a number of times, an actor could be restarted. If it's a negative value, there is not limit.</param>
    /// <param name="timeout">Defines time window for number of retries to occur.</param>
    /// <param name="decider">Used to determine a actor behavior response depending on exception occurred.</param>
    static member AllForOne (decider : Expr<(exn -> Directive)>, ?retries : int, ?timeout : TimeSpan) : SupervisorStrategy = 
        upcast AllForOneStrategy(OptionHelper.optToNullable retries, OptionHelper.optToNullable timeout, ExprDecider decider)
        
module System = 
    /// Creates an actor system with remote deployment serialization enabled.
    let create (name : string) (config : Akka.Configuration.Config) : ActorSystem = 
        let system = ActorSystem.Create(name, config)
        Serialization.exprSerializationSupport system
        system
        
[<AutoOpen>]
module Spawn =

    type SpawnOption = 
        | Deploy of Deploy
        | Router of Akka.Routing.RouterConfig
        | SupervisorStrategy of SupervisorStrategy
        | Dispatcher of string
        | Mailbox of string

    let rec applySpawnOptions (props : Props) (opt : SpawnOption list) : Props = 
        match opt with
        | [] -> props
        | h :: t -> 
            let p = 
                match h with
                | Deploy d -> props.WithDeploy d
                | Router r -> props.WithRouter r
                | SupervisorStrategy s -> props.WithSupervisorStrategy s
                | Dispatcher d -> props.WithDispatcher d
                | Mailbox m -> props.WithMailbox m
            applySpawnOptions p t

    /// <summary>
    /// Spawns an actor using specified actor computation expression, using an Expression AST.
    /// The actor code can be deployed remotely.
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="expr">F# expression compiled down to receive function used by actor for response for incoming request</param>
    /// <param name="options">List of options used to configure actor creation</param>
    let spawne (actorFactory : IActorRefFactory) (name : string) (expr : Expr<Actor<'Message> -> Cont<'Message, 'Returned>>) 
        (options : SpawnOption list) : IActorRef = 
        let e = Linq.Expression.ToExpression(fun () -> new FunActor<'Message, 'Returned>(expr))
        let props = applySpawnOptions (Props.Create e) options
        actorFactory.ActorOf(props, name)

    /// <summary>
    /// Spawns an actor using specified actor computation expression, with custom spawn option settings.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used by actor for handling response for incoming request</param>
    /// <param name="options">List of options used to configure actor creation</param>
    let spawnOpt (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> Cont<'Message, 'Returned>) 
        (options : SpawnOption list) : IActorRef = 
        let e = Linq.Expression.ToExpression(fun () -> new FunActor<'Message, 'Returned>(f))
        let props = applySpawnOptions (Props.Create e) options
        actorFactory.ActorOf(props, name)

    /// <summary>
    /// Spawns an actor using specified actor computation expression.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used by actor for handling response for incoming request</param>
    let spawn (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> Cont<'Message, 'Returned>) : IActorRef = 
        spawnOpt actorFactory name f []

    /// <summary>
    /// Spawns an actor using specified actor quotation, with custom spawn option settings.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used to create a new instance of the actor</param>
    /// <param name="options">List of options used to configure actor creation</param>
    let spawnObjOpt (actorFactory : IActorRefFactory) (name : string) (f : Quotations.Expr<(unit -> #ActorBase)>) 
        (options : SpawnOption list) : IActorRef = 
        let e = Linq.Expression.ToExpression<'Actor> f
        let props = applySpawnOptions (Props.Create e) options
        actorFactory.ActorOf(props, name)

    /// <summary>
    /// Spawns an actor using specified actor quotation.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used to create a new instance of the actor</param>
    let spawnObj (actorFactory : IActorRefFactory) (name : string) (f : Quotations.Expr<(unit -> #ActorBase)>) : IActorRef = 
        spawnObjOpt actorFactory name f []

    /// <summary>
    /// Wraps provided function with actor behavior. 
    /// It will be invoked each time, an actor will receive a message. 
    /// </summary>
    let actorOf (fn : 'Message -> unit) (mailbox : Actor<'Message>) : Cont<'Message, 'Returned> = 
        let rec loop() = 
            actor { 
                let! msg = mailbox.Receive()
                fn msg
                return! loop()
            }
        loop()

    /// <summary>
    /// Wraps provided function with actor behavior. 
    /// It will be invoked each time, an actor will receive a message. 
    /// </summary>
    let actorOf2 (fn : Actor<'Message> -> 'Message -> unit) (mailbox : Actor<'Message>) : Cont<'Message, 'Returned> = 
        let rec loop() = 
            actor { 
                let! msg = mailbox.Receive()
                fn mailbox msg
                return! loop()
            }
        loop()

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
    let monitor (subject: IActorRef) (watcher: ICanWatch) : IActorRef = watcher.Watch subject

    /// <summary>
    /// Orders a <paramref name="watcher"/> to stop monitoring an actor refered by provided <paramref name="subject"/>.
    /// </summary>
    let demonitor (subject: IActorRef) (watcher: ICanWatch) : IActorRef = watcher.Unwatch subject

[<AutoOpen>]
module EventStreaming =

    /// <summary>
    /// Subscribes an actor reference to target channel of the provided event stream.
    /// </summary>
    let subscribe (channel: System.Type) (ref: IActorRef) (eventStream: Akka.Event.EventStream) : bool = eventStream.Subscribe(ref, channel)

    /// <summary>
    /// Unubscribes an actor reference from target channel of the provided event stream.
    /// </summary>
    let unsubscribe (channel: System.Type) (ref: IActorRef) (eventStream: Akka.Event.EventStream) : bool = eventStream.Unsubscribe(ref, channel)

    /// <summary>
    /// Publishes an event on the provided event stream. Event channel is resolved from event's type.
    /// </summary>
    let publish (event: 'Event) (eventStream: Akka.Event.EventStream) : unit = eventStream.Publish event

