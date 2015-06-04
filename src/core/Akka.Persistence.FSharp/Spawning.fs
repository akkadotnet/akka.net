//-----------------------------------------------------------------------
// <copyright file="Spawning.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
namespace Akka.Persistence.FSharp

open System
open Akka.Actor
open Akka.FSharp

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
        
        static member ToExpression(f : System.Linq.Expressions.Expression<System.Func<FunPersistentView<'Event, 'State>>>) = 
            match f with
            | Lambda(_, Invoke(Call(null, Method "ToFSharpFunc", Ar [| Lambda(_, p) |]))) -> 
                Expression.Lambda(p, [||]) :?> System.Linq.Expressions.Expression<System.Func<FunPersistentView<'Event, 'State>>>
            | _ -> failwith "Doesn't match"

[<AutoOpen>]
module Spawn =

    /// <summary>
    /// Spawns a persistent actor instance.
    /// </summary>
    /// <param name="actorFactory">Object responsible for actor instantiation.</param>
    /// <param name="name">Identifies uniquely current actor across different incarnations. It's necessary to identify it's event source.</param>
    /// <param name="aggregate">Aggregate containing state of the actor, but also an event- and command-handling behavior.</param>
    /// <param name="state">Initial state of an actor.</param>
    /// <param name="options">Additional spawning options.</param>
    let spawnPersist (actorFactory : IActorRefFactory) (name : PersistenceId) (state : 'State) 
        (aggregate : Eventsourced<'Command, 'Event, 'State> -> Aggregate<'Command, 'Event, 'State>)
        (options : SpawnOption list) : IActorRef<'Command> = 
        let e = 
            Linq.PersistentExpression.ToExpression
                (fun () -> new FunPersistentActor<'Command, 'Event, 'State>(aggregate, state, name))
        let props = applySpawnOptions (Props.Create e) options
        typed (actorFactory.ActorOf(props, name))

    /// <summary>
    /// Spawns a persistent view instance. Unlike actor's views are readonly versions of statefull, recoverable actors.
    /// </summary>
    /// <param name="actorFactory">Object responsible for actor instantiation.</param>
    /// <param name="name">Identifies uniquely current actor across different incarnations. It's necessary to identify it's event source.</param>
    /// <param name="viewName">Identifies uniquely current view's state. It's different that event source, since many views with different internal states can relate to single event source.</param>
    /// <param name="options">Additional spawning options.</param>
    let spawnView (actorFactory : IActorRefFactory) (viewName : PersistenceId) (name : PersistenceId) 
        (f : View<'Message, 'State> -> Cont<'Message, 'Returned>) (options : SpawnOption list) : IActorRef<'Command> = 
        let e = Linq.PersistentExpression.ToExpression(fun () -> new FunPersistentView<'Event, 'State>(f, name, viewName))
        let props = applySpawnOptions (Props.Create e) options
        typed (actorFactory.ActorOf(props, viewName))

    /// <summary>
    /// Spawns a guaranteed delivery actor. This actor can deliver messages using at-least-once delivery semantics.
    /// </summary>
    /// <param name="actorFactory">Object responsible for actor instantiation.</param>
    /// <param name="name">Identifies uniquely current actor across different incarnations. It's necessary to identify it's event source.</param>
    /// <param name="aggregate">Aggregate containing state of the actor, but also an event- and command-handling behavior.</param>
    /// <param name="state">Initial state of a deliverer.</param>
    /// <param name="options">Additional spawning options.</param>
    let spawnDeliverer (actorFactory : IActorRefFactory) (name : PersistenceId) (state : 'State) 
        (aggregate : Delivery<'Command, 'Event, 'State> -> DeliveryAggregate<'Command, 'Event, 'State>)
        (options : SpawnOption list) : IActorRef<'Command> = 
        let e = 
            Linq.PersistentExpression.ToExpression
                (fun () -> new Deliverer<'Command, 'Event, 'State>(aggregate, state, name))
        let props = applySpawnOptions (Props.Create e) options
        typed (actorFactory.ActorOf(props, name))
