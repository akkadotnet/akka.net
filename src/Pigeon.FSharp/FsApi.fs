module Akka.FSharp
open Akka.Actor


[<AbstractClass>]
type Actor()=
    inherit Akka.Actor.UntypedActor()

let (<!) (actorRef:ActorRef) (msg: obj) =
    actorRef.Tell msg
    ignore()

let (<?) (tell:ICanTell) (msg: obj) =
    tell.Ask msg
    |> Async.AwaitIAsyncResult 
    |> Async.Ignore

/// <summary>
/// Gives access to the next message throu let! binding in
/// actor computation expression.
/// </summary>
type IO<'msg> = | Input

type Cont<'m,'v> =
    | Func of ('m -> Cont<'m,'v>)
    | Return of 'v

/// <summary>
/// The builder for actor computation expression<
/// </summary>
type ActorBuilder() =

    // binds the next message
    member this.Bind(m : IO<'msg>, f :'msg -> _) =
        Func (fun m -> f m)

    // binds the result of another actor computation expression
    member this.Bind(x : Cont<'m,'a>, f :'a -> Cont<'m,'b>) : Cont<'m,'b> =
        match x with
        | Func fx -> Func(fun m -> this.Bind(fx m, f))
        | Return v -> f v
    
    member this.ReturnFrom(x) = x

    member this.Return x = Return x

    member this.Zero() = Return ()

    member this.TryWith(f:unit -> Cont<'m,'a>,c: exn -> Cont<'m,'a>): Cont<'m,'a> =
        try
            true, f()
        with
        | ex -> false, c ex
        |> function
           | true, Func fn -> Func(fun m -> this.TryWith((fun () -> fn m), c) )
           | _, v -> v


    member this.While(condition: unit -> bool, f: unit -> Cont<'m,unit>) : Cont<'m, unit> =
        if condition() then
            match f() with
            | Func fn -> Func(fun m -> 
                            fn m |> ignore
                            this.While(condition, f))
            | v -> this.While(condition, f)
        else
            Return ()

    member this.Delay(f: unit -> Cont<_,_>) = 
        f

    member this.Run(f: unit -> Cont<_,_>) = f()
    member this.Run(f: Cont<_,_>) = f

    member this.Combine(f: unit -> Cont<'m, _>,g: unit -> Cont<'m,'v>) : Cont<'m,'v> =
        match f() with
        | Func fx -> Func(fun m -> this.Combine((fun() -> fx m), g))
        | Return _ -> g()

    member this.Combine(f: Cont<'m, _>,g: unit -> Cont<'m,'v>) : Cont<'m,'v> =
        match f with
        | Func fx -> Func(fun m -> this.Combine(fx m, g))
        | Return _ -> g()

    member this.Combine(f: unit -> Cont<'m, _>,g: Cont<'m,'v>) : Cont<'m,'v> =
        match f() with
        | Func fx -> Func(fun m -> this.Combine((fun() -> fx m), g))
        | Return _ -> g

    member this.Combine(f: Cont<'m, _>,g: Cont<'m,'v>) : Cont<'m,'v> =
        match f with
        | Func fx -> Func(fun m -> this.Combine(fx m, g))
        | Return _ -> g


/// <summary>
/// Builds an actor message handler using an actor expression syntax.
/// </summary>
let actor = ActorBuilder()

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Linq.QuotationEvaluation

type FunActor<'m,'v>(actor: IO<'m> -> Cont<'m,'v>) =
    inherit ActorBase()
    let mutable state = actor Input

    new (actor: Expr<IO<'m> -> Cont<'m,'v>>) = FunActor(actor.Compile() ())
    override x.OnReceive(msg) =
        let message = msg :?> 'm
        match state with
        | Func f -> state <- f message
        | Return v -> x.PostStop()

    

module Linq =
    open System.Linq.Expressions

    let (|Lambda|_|) (e:Expression) =
        match e with
        | :? LambdaExpression as l -> Some(l.Parameters, l.Body)
        | _ -> None
    let (|Call|_|) (e:Expression) =
        match e with
        | :? MethodCallExpression as c -> Some(c.Object,c.Method,c.Arguments)
        | _ -> None

    let (|Method|) (e:System.Reflection.MethodInfo) = e.Name
    let (|Invoke|_|) =
        function
        | Call(o,Method("Invoke"),_) -> Some o
        | _ -> None
    let (|Ar|) (p:System.Collections.ObjectModel.ReadOnlyCollection<Expression>) = Array.ofSeq p
    type Expression =
        static member ToExpression (f:System.Linq.Expressions.Expression<System.Func<FunActor<'m,'v>>>) =
            match f with
            | Lambda(_,Invoke(Call(null, Method "ToFSharpFunc", Ar [|Lambda(_,p)|]))) ->
                Expression.Lambda(p,[||]) :?> System.Linq.Expressions.Expression<System.Func<FunActor<'m,'v>>>
            | _ -> failwith "Doesn't match"

module Serialization =
    open Nessos.FsPickler

    open Akka.Serialization
    open Quotations.Patterns

    type ExprSerializer(system) =
        
        inherit Serializer(system)
        

        let fsp = new FsPickler()
        override x.Identifier = 9
        override x.IncludeManifest = true

        override x.ToBinary(o) =
            use stream = new System.IO.MemoryStream()
            fsp.Serialize(o.GetType(),stream, o)
            stream.ToArray()           
         
        override x.FromBinary(bytes, t) =
            use stream = new System.IO.MemoryStream(bytes)
            fsp.Deserialize(t, stream)
          
module Configuration =
    let parse = Akka.Configuration.ConfigurationFactory.ParseString

module System =
    /// <summary>
    /// Creates an actor system with remote deployment serialization enabled.
    /// </summary>
    /// <param name="name">The system name.</param>
    /// <param name="configStr">The configuration</param>
    let create name (config: Configuration.Config) =
        let system = ActorSystem.Create(name, config)
        let serializer = new Serialization.ExprSerializer(system)
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
let spawne (system:ActorSystem) name (f: Expr<IO<'m> -> Cont<'m,'v>>)  =
   let e = Linq.Expression.ToExpression(fun () -> new FunActor<'m,'v>(f))
   system.ActorOf(Props.Create(e), name)

/// <summary>
/// Spawns an actor using specified actor computation expression.
/// The actor can only be used locally. 
/// </summary>
/// <param name="system">The system used to spawn the actor</param>
/// <param name="name">The actor instance nane</param>
/// <param name="f">the actor's message handling function.</param>
let spawn (system:ActorSystem) name (f: IO<'m> -> Cont<'m,'v>)  =
   let e = Linq.Expression.ToExpression(fun () -> new FunActor<'m,'v>(f))
   system.ActorOf(Props.Create(e), name)
