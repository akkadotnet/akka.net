module Akka.FSharp

open Akka.Actor
open System

type IO<'msg> = 
    | Input

(** Exposes an Akka.NET actor APi accessible from inside of F# continuations - see: `Cont<'m, 'v>`. *)
[<Interface>]
type Actor<'msg> = 
    inherit ActorRefFactory
    (** Explicitly retrieves next incoming message from the mailbox. *)
    abstract Receive : unit -> IO<'msg>
    (** Get `ActorRef` for the current actor. *)
    abstract Self : ActorRef
    (** Get current actor context. *)
    abstract Context : IActorContext
    (** Function, which returns a sender of current message or NoSender, if none could be determined. *)
    abstract Sender : unit -> ActorRef
    (** Explicit signalization of unhandled message *)
    abstract Unhandled : 'msg -> unit

[<AbstractClass>]
type Actor() = 
    inherit UntypedActor()

(** 
Returns an instance of `ActorSelection` for specified path. 
If no matching receiver will be found, a NoSender instance will be returned. 
*)
let inline select (path : string) (selector : ActorRefFactory) : ActorSelection = selector.ActorSelection path
(** 
Unidirectional send operator. 
Sends a message object directly to actor tracked by actorRef. 
*)
let inline (<!) (actorRef : #ICanTell) (msg : obj) : unit = actorRef.Tell(msg, ActorCell.GetCurrentSelfOrNoSender())
(** 
Bidirectional send operator. Sends a message object directly to actor 
tracked by actorRef and awaits for response send back from corresponding actor. 
*)
let inline (<?) (tell : #ICanTell) (msg : obj) = tell.Ask msg |> Async.AwaitTask

[<AutoOpen>]
module Logging = 
    open Akka.Event
    open Microsoft.FSharp.Core.Printf
    
    let inline private loggerFor (context : IActorContext) : LoggingAdapter = Logging.GetLogger(context)
    
    (** Logs a message using configured Akka logger. *)
    let log (level : LogLevel) (mailbox : Actor<'a>) (fmt : StringFormat<string>) : unit = 
        let logger = loggerFor mailbox.Context
        logger.Log(level, sprintf fmt)
    
    (** Logs a message at Debug level using configured Akka logger. *)
    let logDebug() = log (LogLevel.DebugLevel)
    (** Logs a message at Info level using configured Akka logger. *)
    let logInfo() = log (LogLevel.InfoLevel)
    (** Logs a message at Warning level using configured Akka logger. *)
    let logWarning() = log (LogLevel.WarningLevel)
    (** Logs a message at Error level using configured Akka logger. *)
    let logError() = log (LogLevel.ErrorLevel)
    (** Logs an exception message at Error level using configured Akka logger. *)
    let logException context (e : exn) = log (LogLevel.ErrorLevel) context (StringFormat<string>(e.Message))

type ActorPath with
    
    (** Perform parsing string into valid `ActorPath`. Returns tuple, which first argument informs about success of the operations, while second one may contain a parsed value. *)
    static member TryParse(path : string) : bool * ActorPath = 
        let mutable actorPath : ActorPath = null
        if ActorPath.TryParse(path, &actorPath) then (true, actorPath)
        else (false, actorPath)
    
    (** Perform parsing string into valid `Address`. Returns tuple, which first argument informs about success of the operations, while second one may contain a parsed value. *)
    static member TryParseAddress(path : string) : bool * Address = 
        let mutable address : Address = null
        if ActorPath.TryParseAddress(path, &address) then (true, address)
        else (false, address)

(** Gives access to the next message throu let! binding in actor computation expression. *)
type Cont<'m, 'v> = 
    | Func of ('m -> Cont<'m, 'v>)
    | Return of 'v

(** The builder for actor computation expression *)
type ActorBuilder() = 
    (** binds the next message *)
    member this.Bind(m : IO<'msg>, f : 'msg -> _) = Func(fun m -> f m)
    
    (** binds the result of another actor computation expression *)
    member this.Bind(x : Cont<'m, 'a>, f : 'a -> Cont<'m, 'b>) : Cont<'m, 'b> = 
        match x with
        | Func fx -> Func(fun m -> this.Bind(fx m, f))
        | Return v -> f v
    
    member this.ReturnFrom(x) = x
    member this.Return x = Return x
    member this.Zero() = Return()
    
    member this.TryWith(f : unit -> Cont<'m, 'a>, c : exn -> Cont<'m, 'a>) : Cont<'m, 'a> = 
        try 
            true, f()
        with ex -> false, c ex
        |> function 
        | true, Func fn -> Func(fun m -> this.TryWith((fun () -> fn m), c))
        | _, v -> v
    
    member this.TryFinally(f : unit -> Cont<'m, 'a>, fnl : unit -> unit) : Cont<'m, 'a> = 
        try 
            match f() with
            | Func fn -> Func(fun m -> this.TryFinally((fun () -> fn m), fnl))
            | r -> 
                fnl()
                r
        with ex -> 
            fnl()
            reraise()
    
    member this.Using(d : #IDisposable, f : _ -> Cont<'m, 'v>) : Cont<'m, 'v> = 
        this.TryFinally((fun () -> f d), 
                        fun () -> 
                            if d <> null then d.Dispose())
    
    member this.While(condition : unit -> bool, f : unit -> Cont<'m, unit>) : Cont<'m, unit> = 
        if condition() then 
            match f() with
            | Func fn -> 
                Func(fun m -> 
                    fn m |> ignore
                    this.While(condition, f))
            | v -> this.While(condition, f)
        else Return()
    
    member this.For(source : 's seq, f : 's -> Cont<'m, unit>) : Cont<'m, unit> = 
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
    
    member this.Delay(f : unit -> Cont<_, _>) = f
    member this.Run(f : unit -> Cont<_, _>) = f()
    member this.Run(f : Cont<_, _>) = f
    
    member this.Combine(f : unit -> Cont<'m, _>, g : unit -> Cont<'m, 'v>) : Cont<'m, 'v> = 
        match f() with
        | Func fx -> Func(fun m -> this.Combine((fun () -> fx m), g))
        | Return _ -> g()
    
    member this.Combine(f : Cont<'m, _>, g : unit -> Cont<'m, 'v>) : Cont<'m, 'v> = 
        match f with
        | Func fx -> Func(fun m -> this.Combine(fx m, g))
        | Return _ -> g()
    
    member this.Combine(f : unit -> Cont<'m, _>, g : Cont<'m, 'v>) : Cont<'m, 'v> = 
        match f() with
        | Func fx -> Func(fun m -> this.Combine((fun () -> fx m), g))
        | Return _ -> g
    
    member this.Combine(f : Cont<'m, _>, g : Cont<'m, 'v>) : Cont<'m, 'v> = 
        match f with
        | Func fx -> Func(fun m -> this.Combine(fx m, g))
        | Return _ -> g

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Linq.QuotationEvaluation

type FunActor<'m, 'v>(actor : Actor<'m> -> Cont<'m, 'v>, strategy : SupervisorStrategy) as self = 
    inherit UntypedActor()
    
    let mutable state = 
        let self' = self.Self
        let context' = UntypedActor.Context :> IActorContext
        actor { new Actor<'m> with
                    member this.Receive() = Input
                    member this.Self = self'
                    member this.Context = context'
                    member this.Sender() = self.Sender()
                    member this.Unhandled msg = self.Unhandled msg
                    member this.ActorOf(props, name) = context'.ActorOf(props, name)
                    member this.ActorSelection(path : string) = context'.ActorSelection(path)
                    member this.ActorSelection(path : ActorPath) = context'.ActorSelection(path) }
    
    new(actor : Expr<Actor<'m> -> Cont<'m, 'v>>) = FunActor(actor.Compile () ())
    new(actor : Actor<'m> -> Cont<'m, 'v>) = FunActor(actor, null)
    member x.Sender(): ActorRef = base.Sender
    member x.Unhandled(msg : 'm): unit = base.Unhandled msg
    
    override x.SupervisorStrategy(): SupervisorStrategy = 
        if strategy <> null then strategy
        else base.SupervisorStrategy()
    
    override x.OnReceive(msg: obj): unit = 
        let message = msg :?> 'm
        match state with
        | Func f -> state <- f message
        | Return v -> x.PostStop()

(** 
Builds an actor message handler using an actor expression syntax. 

Example:
```
spawn system "exampleActor" 
    <|fun mailbox ->
        let rec loop'() = 
            actor { 
                let! msg = mailbox.Receive()
                // some computations
                return! loop'()
            }
        loop'()
```
*)
let actor = ActorBuilder()

module Linq = 
    open System.Linq.Expressions
    
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
    
    type Expression = 
        static member ToExpression(f : System.Linq.Expressions.Expression<System.Func<FunActor<'m, 'v>>>) = 
            match f with
            | Lambda(_, Invoke(Call(null, Method "ToFSharpFunc", Ar [| Lambda(_, p) |]))) -> 
                Expression.Lambda(p, [||]) :?> System.Linq.Expressions.Expression<System.Func<FunActor<'m, 'v>>>
            | _ -> failwith "Doesn't match"

module Serialization = 
    open Nessos.FsPickler
    open Akka.Serialization
    open Quotations.Patterns
    
    type ExprSerializer(system) = 
        inherit Serializer(system)
        let fsp = FsPickler.CreateBinary()
        override x.Identifier = 9
        override x.IncludeManifest = true
        
        override x.ToBinary(o) = 
            use stream = new System.IO.MemoryStream()
            fsp.Serialize(o.GetType(), stream, o)
            stream.ToArray()
        
        override x.FromBinary(bytes, t) = 
            use stream = new System.IO.MemoryStream(bytes)
            fsp.Deserialize(t, stream)

module Configuration = 
    let parse = Akka.Configuration.ConfigurationFactory.ParseString
    let defaultConfig = Akka.Configuration.ConfigurationFactory.Default

module Strategy = 
    (** 
    Returns a supervisor strategy appliable only to child actor which faulted during execution.

    - `decider` is a function used to determine a actor behavior response depending on exception occurred.
    *)
    let oneForOne (decider : Exception -> Directive) : SupervisorStrategy = 
        OneForOneStrategy(System.Func<_, _>(decider)) :> SupervisorStrategy
    (**
    Returns a supervisor strategy appliable only to child actor which faulted during execution.

    - `retries` defines a number of times, actor could be restarted. If negative, there is not limit.
    - `timeout` defines time window for number of retries to occur.
    - `decider` is a function used to determine a actor behavior response depending on exception occurred.
    *)
    let oneForOne2 (retries : int) (timeout : TimeSpan) (decider : Exception -> Directive) : SupervisorStrategy = 
        OneForOneStrategy(Nullable(retries), Nullable(timeout), System.Func<_, _>(decider)) :> SupervisorStrategy
    (**
    Returns a supervisor strategy appliable only each supervised actor when any of them had faulted during execution.
    
    - `decider` is a function used to determine a actor behavior response depending on exception occurred.
    *)
    let allForOne (decider : Exception -> Directive) : SupervisorStrategy = 
        AllForOneStrategy(System.Func<_, _>(decider)) :> SupervisorStrategy
    (**
    Returns a supervisor strategy appliable only each supervised actor when any of them had faulted during execution.

    - `retries` defines a number of times, actor could be restarted. If negative, there is not limit.
    - `timeout` defines time window for number of retries to occur.
    - `decider` is a function used to determine a actor behavior response depending on exception occurred.
    *)
    let allForOne2 (retries : int) (timeout : TimeSpan) (decider : Exception -> Directive) : SupervisorStrategy = 
        AllForOneStrategy(Nullable(retries), Nullable(timeout), System.Func<_, _>(decider)) :> SupervisorStrategy

module System = 
    (** Creates an actor system with remote deployment serialization enabled. *)
    let create (name : string) (config : Configuration.Config) : ActorSystem = 
        let system = ActorSystem.Create(name, config)
        let serializer = new Serialization.ExprSerializer(system :?> ExtendedActorSystem)
        system.Serialization.AddSerializer(serializer)
        system.Serialization.AddSerializationMap(typeof<Expr>, serializer)
        system

(**
Spawns an actor using specified actor computation expression, using an Expression AST.
The actor code can be deployed remotely.

Example:
*)
let spawne (system : ActorRefFactory) (name: string) (f : Expr<Actor<'m> -> Cont<'m, 'v>>) : ActorRef = 
    let e = Linq.Expression.ToExpression(fun () -> new FunActor<'m, 'v>(f))
    system.ActorOf(Props.Create(e), name)

(**
Spawns an actor using specified actor computation expression, with custom strategy supervisor.
The actor can only be used locally. 

Example:
```
let parent =
    spawns system "master"
    // below we define OneForOneStrategy to handle specific exceptions 
    // incoming from child actors
    <| (Strategy.oneForOne <| fun e ->
        match e with
        | :? ArithmeticException -> Directive.Resume
        | :? ArgumentException   -> Directive.Stop
        | _                      -> Directive.Escalate)
    <| fun mailbox ->
        let worker = spawn mailbox "worker" <| workerFun
        let rec loop() = actor {
            let! msg = mailbox.Receive()
            // parent logic
            return! loop() }
        loop()
```
*)
let spawns (system : ActorRefFactory) (name: string) (strategy : SupervisorStrategy) (f : Actor<'m> -> Cont<'m, 'v>) : ActorRef = 
    let e = Linq.Expression.ToExpression(fun () -> new FunActor<'m, 'v>(f, strategy))
    system.ActorOf(Props.Create(e), name)

(**
Spawns an actor using specified actor computation expression.
The actor can only be used locally. 

Example:
```
use system = System.create "sys" configuration
spawn system "parent" 
    <| fun mailbox ->
        let rec loop'() = 
            actor { 
                let! msg = mailbox.Receive()
                // it's possible to use spawn to create actor hierarchies
                let child = spawn mailbox "child" <| childActor
                return! loop'()
            }
        loop'()
```
*)
let spawn (system : ActorRefFactory) (name: string) (f : Actor<'m> -> Cont<'m, 'v>) : ActorRef = spawns system name null f

(** 
Wraps provided function with actor behavior. It will be invoked each time, 
an actor will receive a message. 

Example:
```
let exampleFunction msg = printfn "%A" msg
let actorRef = spawn system "example" (actorOf exampleFunction)
```
*)
let actorOf (fn : 'a -> unit) (mailbox : Actor<'a>) : Cont<'a, 'b> = 
    let rec loop'() = 
        actor { 
            let! msg = mailbox.Receive()
            fn msg
            return! loop'()
        }
    loop'()

(** 
Wraps provided function with actor behavior. It will be invoked each time, 
an actor will receive a message. Additionally it will get an actor behavior object as first parameter.

Example:
```
let exampleFunction (mailbox: Actor<'a>) msg = 
    printfn "%A" msg
    mailbox.Sender() <! msg

let actorRef = spawn system "example" (actorOf2 exampleFunction)
```
*)
let actorOf2 (fn : Actor<'a> -> 'a -> unit) (mailbox : Actor<'a>) : Cont<'a, 'b> = 
    let rec loop'() = 
        actor { 
            let! msg = mailbox.Receive()
            fn mailbox msg
            return! loop'()
        }
    loop'()

(** 
Creates an actor-like object, which could be interrogated from the outside. 
Usually it's used to spy on other actors lifecycle.
Most of the inbox methods works in thread-blocking manner.
*)
let inbox (system: ActorSystem) : Inbox = Inbox.Create system
