//-----------------------------------------------------------------------
// <copyright file="Spawning.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
namespace Akka.FSharp

open Akka.Actor
open System
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Linq.QuotationEvaluation

[<RequireQualifiedAccess>]
module Configuration = 
    /// Parses provided HOCON string into a valid Akka configuration object.
    let parse = Akka.Configuration.ConfigurationFactory.ParseString
    
    /// Returns default Akka configuration.
    let defaultConfig = Akka.Configuration.ConfigurationFactory.Default
    
    /// Loads Akka configuration from the project's .config file.
    let load = Akka.Configuration.ConfigurationFactory.Load

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
    let spawne (actorFactory : IActorRefFactory) (name : string) 
        (expr : Expr<Actor<'Message> -> Cont<'Message, 'Returned>>) (options : SpawnOption list) : IActorRef<'Message> = 
        let e = Linq.Expression.ToExpression(fun () -> new FunActor<'Message, 'Returned>(expr))
        let props = applySpawnOptions (Props.Create e) options
        typed (actorFactory.ActorOf(props, name)) :> IActorRef<'Message>
    
    /// <summary>
    /// Spawns an actor using specified actor computation expression, with custom spawn option settings.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used by actor for handling response for incoming request</param>
    /// <param name="options">List of options used to configure actor creation</param>
    let spawnOpt (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> Cont<'Message, 'Returned>) 
        (options : SpawnOption list) : IActorRef<'Message> = 
        let e = Linq.Expression.ToExpression(fun () -> new FunActor<'Message, 'Returned>(f))
        let props = applySpawnOptions (Props.Create e) options
        typed (actorFactory.ActorOf(props, name)) :> IActorRef<'Message>
    
    /// <summary>
    /// Spawns an actor using specified actor computation expression.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used by actor for handling response for incoming request</param>
    let spawn (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> Cont<'Message, 'Returned>) : IActorRef<'Message> = 
        spawnOpt actorFactory name f []
    
    /// <summary>
    /// Spawns an actor using specified actor quotation, with custom spawn option settings.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used to create a new instance of the actor</param>
    /// <param name="options">List of options used to configure actor creation</param>
    let spawnObjOpt (actorFactory : IActorRefFactory) (name : string) (f : Quotations.Expr<unit -> #ActorBase>) 
        (options : SpawnOption list) : IActorRef<'Message> = 
        let e = Linq.Expression.ToExpression<'Actor> f
        let props = applySpawnOptions (Props.Create e) options
        typed (actorFactory.ActorOf(props, name)) :> IActorRef<'Message>
    
    /// <summary>
    /// Spawns an actor using specified actor quotation.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used to create a new instance of the actor</param>
    let spawnObj (actorFactory : IActorRefFactory) (name : string) (f : Quotations.Expr<unit -> #ActorBase>) : IActorRef<'Message> = 
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
