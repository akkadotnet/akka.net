module Akka.FSharp

open Akka.Actor
open System

type IO<'msg> = 
    | Input

[<Interface>]
type Actor<'msg> = 
    abstract Receive : unit -> IO<'msg>
    abstract Self : ActorRef
    abstract Context : IActorContext
    abstract Sender : unit -> ActorRef
    abstract Unhandled : 'msg -> unit

[<AbstractClass>]
type Actor() = 
    inherit Akka.Actor.UntypedActor()

let inline select (path : string) : ActorSelection = ActorSelection(ActorCell.GetCurrentSelfOrNoSender(), path)
let inline (<!) (actorRef : #ICanTell) (msg : obj) = actorRef.Tell(msg, ActorCell.GetCurrentSelfOrNoSender())
let inline (!>) (msg : obj) (actorRef : #ICanTell) = actorRef <! msg
let inline (<?) (tell : #ICanTell) (msg : obj) = tell.Ask msg |> Async.AwaitTask
let inline (?>) (msg : obj) (actorRef : #ICanTell) = actorRef <? msg

module ActorSelection = 
    let inline (<!) (actorPath : string) (msg : obj) = (select actorPath) <! msg
    let inline (<?) (actorPath : string) (msg : obj) = (select actorPath) <? msg

type ActorPath with

    static member TryParse(path : string) = 
        let mutable actorPath : ActorPath = null
        if ActorPath.TryParse(path, &actorPath) then (true, actorPath)
        else (false, actorPath)
    
    static member TryParseAddress(path : string) = 
        let mutable address : Address = null
        if ActorPath.TryParseAddress(path, &address) then (true, address)
        else (false, address)

/// <summary>
/// Gives access to the next message throu let! binding in
/// actor computation expression.
/// </summary>
type Cont<'m, 'v> = 
    | Func of ('m -> Cont<'m, 'v>)
    | Return of 'v

/// <summary>
/// The builder for actor computation expression<
/// </summary>
type ActorBuilder() = 
    // binds the next message
    member this.Bind(m : IO<'msg>, f : 'msg -> _) = Func(fun m -> f m)
    
    // binds the result of another actor computation expression
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
                    member this.Unhandled msg = self.Unhandled msg }
    
    new(actor : Expr<Actor<'m> -> Cont<'m, 'v>>) = FunActor(actor.Compile () ())
    new(actor : Actor<'m> -> Cont<'m, 'v>) = FunActor(actor, null)
    member x.Sender() = base.Sender
    member x.Unhandled(msg : 'm) = base.Unhandled msg
    
    override x.SupervisorStrategy() = 
        if strategy <> null then strategy
        else base.SupervisorStrategy()
    
    override x.OnReceive(msg) = 
        let message = msg :?> 'm
        match state with
        | Func f -> state <- f message
        | Return v -> x.PostStop()

/// <summary>
/// Builds an actor message handler using an actor expression syntax.
/// </summary>
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
    /// <summary>
    /// Returns a builder function returning OneForOneStrategy supervisor based on provided decider func.
    /// </summary>
    /// <param name="decider">Supervisor strategy behavior decider function.</param>
    let oneForOne (decider : Exception -> Directive) = 
        OneForOneStrategy(System.Func<_, _>(decider)) :> SupervisorStrategy
    
    /// <summary>
    /// Returns a builder function returning OneForOneStrategy supervisor based on provided decider func.
    /// </summary>
    /// <param name="retries">Number of times, actor could be restarted. If negative, there is not limit.</param>
    /// <param name="timeout">Time window for number of retries to occur.</param>
    /// <param name="decider">Supervisor strategy behavior decider function.</param>
    let oneForOne' retries timeout (decider : Exception -> Directive) = 
        OneForOneStrategy(retries, timeout, System.Func<_, _>(decider)) :> SupervisorStrategy
    
    /// <summary>
    /// Returns a builder function returning AllForOneStrategy supervisor based on provided decider func.
    /// </summary>
    /// <param name="decider">Supervisor strategy behavior decider function.</param>
    let allForOne (decider : Exception -> Directive) = 
        AllForOneStrategy(System.Func<_, _>(decider)) :> SupervisorStrategy
    
    /// <summary>
    /// Returns a builder function returning AllForOneStrategy supervisor based on provided decider func.
    /// </summary>
    /// <param name="retries">Number of times, actor could be restarted. If negative, there is not limit.</param>
    /// <param name="timeout">Time window for number of retries to occur.</param>
    /// <param name="decider">Supervisor strategy behavior decider function.</param>
    let allForOne' retries timeout (decider : Exception -> Directive) () = 
        AllForOneStrategy(retries, timeout, System.Func<_, _>(decider)) :> SupervisorStrategy

module System = 
    /// <summary>
    /// Creates an actor system with remote deployment serialization enabled.
    /// </summary>
    /// <param name="name">The system name.</param>
    /// <param name="configStr">The configuration</param>
    let create name (config : Configuration.Config) = 
        let system = ActorSystem.Create(name, config)
        let serializer = new Serialization.ExprSerializer(system :?> ExtendedActorSystem)
        system.Serialization.AddSerializer(serializer)
        system.Serialization.AddSerializationMap(typeof<Expr>, serializer)
        system

/// <summary>
/// Spawns an actor using specified actor computation expression, using an Expression AST.
/// The actor code can be deployed remotely.
/// </summary>
/// <param name="system">The system used to spawn the actor</param>
/// <param name="name">The actor instance nane</param>
/// <param name="f">the actor's message handling function.</param>
let spawne (system : ActorSystem) name (f : Expr<Actor<'m> -> Cont<'m, 'v>>) = 
    let e = Linq.Expression.ToExpression(fun () -> new FunActor<'m, 'v>(f))
    system.ActorOf(Props.Create(e), name)

/// <summary>
/// Spawns an actor using specified actor computation expression, with strategy supervisor factory.
/// The actor can only be used locally. 
/// </summary>
/// <param name="system">The system used to spawn the actor</param>
/// <param name="name">The actor instance name</param>
/// <param name="strategy">Function used to generate supervisor strategy</param>
/// <param name="f">the actor's message handling function.</param>
let spawns (system : ActorSystem) name (strategy : SupervisorStrategy) (f : Actor<'m> -> Cont<'m, 'v>) = 
    let e = Linq.Expression.ToExpression(fun () -> new FunActor<'m, 'v>(f, strategy))
    system.ActorOf(Props.Create(e), name)

/// <summary>
/// Spawns an actor using specified actor computation expression.
/// The actor can only be used locally. 
/// </summary>
/// <param name="system">The system used to spawn the actor</param>
/// <param name="name">The actor instance nane</param>
/// <param name="f">the actor's message handling function.</param>
let spawn (system : ActorSystem) name (f : Actor<'m> -> Cont<'m, 'v>) = spawns system name null f

[<AutoOpen>]
module Actors = 
    // declare extension methods for Actor interface
    type Actor<'msg> with
        
        /// <summary>
        /// Implementation of spawne method using actor-local context.
        /// Actor refs returned this way are considered children of current actor.
        /// </summary>
        member this.spawne (system : ActorSystem) name (f : Expr<Actor<'m> -> Cont<'m, 'v>>) = 
            let e = Linq.Expression.ToExpression(fun () -> new FunActor<'m, 'v>(f))
            this.Context.ActorOf(Props.Create(e), name)
        
        /// <summary>
        /// Implementation of spawns method using actor-local context.
        /// Actor refs returned this way are considered children of current actor. 
        /// </summary>
        /// <param name="name">The actor instance name to be created as child of current actor</param>
        /// <param name="strategy">Function used to generate supervisor strategy</param>
        /// <param name="f">the actor's message handling function.</param>
        member this.spawns name (strategy : SupervisorStrategy) (f : Actor<'m> -> Cont<'m, 'v>) = 
            let e = Linq.Expression.ToExpression(fun () -> new FunActor<'m, 'v>(f, strategy))
            this.Context.ActorOf(Props.Create(e), name)
        
        /// <summary>
        /// Implementation of spawn method using actor-local context.
        /// Actor refs returned this way are considered children of current actor.
        /// </summary>
        member this.spawn name (f : Actor<'m> -> Cont<'m, 'v>) = this.spawns name null f
