//-----------------------------------------------------------------------
// <copyright file="FsApi.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.FSharp

open System
open Akka.Actor

[<AutoOpen>]
module Rpc =


    let [<Literal>] MAX_WAIT_TIME_SECS = 60.0

    type private AsyncResult<'R> = | Result of 'R

    type Actor<'Message> internal (orig: Akka.FSharp.Actors.Actor<obj>) =
        
        /// <summary>
        /// receive a typed message on the mailbox
        /// </summary>
        member x.Receive() =
            let rec loop () = actor {
                let! msg = orig.Receive()
                match msg with
                | :? 'Message as m -> return m
                | _ ->
                    orig.Unhandled msg
                    return! loop()
            }
            loop()

        /// <summary>
        /// wait for an async result (non blocking)
        /// </summary>
        member x.AsyncResult (exp: Async<'Out>) =
            async {
                let! result = exp
                orig.Self.Tell (Result result, orig.Self)
            } |> Async.Start
    
            let rec loop() = actor {
                let! msg = orig.Receive()
                match msg with
                | :? 'Out as m when orig.Sender() = orig.Self ->
                    let resp = m
                    orig.UnstashAll()
                    return resp

                | _ ->
                    orig.Stash()
                    return! loop()
            }

            loop ()

        /// <summary>
        /// perform an RPC call and wait for the result
        /// </summary>
        member x.Rpc<'Out> (remote: IActorRef, msg: obj, ?timespan: TimeSpan) =
            let req =
                remote.Ask
                    (box msg
                    , match timespan with
                      | Some t -> t
                      | None   -> TimeSpan.FromSeconds MAX_WAIT_TIME_SECS)

            x.AsyncResult req

[<AutoOpen>]
module Spawn =
    /// <summary>
    /// Spawns an actor using specified actor computation expression, with custom spawn option settings.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used by actor for handling response for incoming request</param>
    /// <param name="options">List of options used to configure actor creation</param>
    let spawnOpt (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> Cont<obj, 'Returned>) 
        (options : SpawnOption list) : IActorRef = 
        let nf (shadAct : Akka.FSharp.Actors.Actor<obj>) =
            let act = Rpc.Actor<'Message>(shadAct)
            f act

        let e = Linq.Expression.ToExpression(fun () -> new FunActor<obj, 'Returned>(nf))
        let props = applySpawnOptions (Props.Create e) options
        actorFactory.ActorOf(props, name)

    /// <summary>
    /// Spawns an actor using specified actor computation expression.
    /// The actor can only be used locally. 
    /// </summary>
    /// <param name="actorFactory">Either actor system or parent actor</param>
    /// <param name="name">Name of spawned child actor</param>
    /// <param name="f">Used by actor for handling response for incoming request</param>
    let spawn (actorFactory : IActorRefFactory) (name : string) (f : Actor<'Message> -> Cont<obj, 'Returned>) : IActorRef = 
        spawnOpt actorFactory name f []