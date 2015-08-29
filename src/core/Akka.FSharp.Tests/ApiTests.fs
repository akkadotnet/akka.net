//-----------------------------------------------------------------------
// <copyright file="ApiTests.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

module Akka.FSharp.Tests.ApiTests

open Akka.FSharp
open Akka.Actor
open System
open Xunit


[<Fact>]
let ``configuration loader should load data from app.config`` () =
    let config = Configuration.load()
    config.HasPath "akka.test.value" 
    |> equals true
    config.GetInt "akka.test.value"
    |> equals 10

[<Fact>]
let ``can serialize expression decider`` () =
    let decider = ExprDecider <@ fun e -> Directive.Resume @>
    use sys = System.create "system" (Configuration.defaultConfig())
    let serializer = sys.Serialization.FindSerializerFor decider
    let bytes = serializer.ToBinary decider
    let des = serializer.FromBinary (bytes, typeof<ExprDecider>) :?> IDecider
    des.Decide (Exception())
    |> equals (Directive.Resume)

type TestUnion = 
    | A of string
    | B of int * string

type TestUnion2 = 
    | C of string * TestUnion
    | D of int

[<Fact>]
let ``can serialize and deserialize discriminated unions over remote nodes`` () =     
    let remoteConfig port = 
        sprintf """
        akka { 
            actor {
                ask-timeout = 5s
                provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            }
            remote {
                helios.tcp {
                    port = %i
                    hostname = localhost
                }
            }
        }
        """ port
        |> Configuration.parse

    use server = System.create "server-system" (remoteConfig 9911)
    use client = System.create "client-system" (remoteConfig 0)

    let aref = 
        spawne client "a-1" <@ actorOf2 (fun mailbox msg -> 
               match msg with
               | C("a-11", B(11, "a-12")) -> mailbox.Sender() <! msg
               | _ -> mailbox.Unhandled msg) @>
            [SpawnOption.Deploy (Deploy(RemoteScope (Address.Parse "akka.tcp://server-system@localhost:9911")))]
    let msg = C("a-11", B(11, "a-12"))
    let response = aref <? msg |> Async.RunSynchronously
    response
    |> equals msg

[<Fact>]
let ``can override PreStart method when starting actor with computation expression`` () =
    
    let preStartCalled = ref false
    let preStart = Some(fun (baseFn : unit -> unit) -> preStartCalled := true)
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawnOvrd system "actor" 
        <| actorOf2 (fun mailbox msg ->
                mailbox.Sender() <! msg)
        <| {defOvrd with PreStart = preStart}
    let response = actor <? "msg" |> Async.RunSynchronously
    (!preStartCalled, response) |> equals (true, "msg")

[<Fact>]
let ``can override PostStop methods when starting actor with computation expression`` () =
    
    let postStopCalled = ref false
    let postStop = Some(fun (baseFn : unit -> unit) -> postStopCalled := true)
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawnOvrd system "actor" 
        <| actorOf2 (fun mailbox msg ->
                mailbox.Sender() <! msg)
        <| {defOvrd with PostStop = postStop}
    actor <! PoisonPill.Instance
    system.Stop(actor)
    system.Shutdown()
    system.AwaitTermination()
    (!postStopCalled) |> equals (true)

[<Fact>]
let ``can override PreRestart methods when starting actor with computation expression`` () =
    
    let preRestartCalled = ref false
    let preRestart = Some(fun (baseFn : exn * obj -> unit) -> preRestartCalled := true)
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawnOptOvrd system "actor3" 
        <| actorOf2 (fun mailbox (msg : string) ->
                if msg = "restart" then
                    failwith "System must be restarted"
                else
                    mailbox.Sender() <! msg)
        <| [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun error ->
                Directive.Restart)) ]
        <| {defOvrd with PreRestart = preRestart}
    actor <! "restart"
    let response = actor <? "msg" |> Async.RunSynchronously
    system.Shutdown()
    system.AwaitTermination()
    (!preRestartCalled, response) |> equals (true, "msg")

[<Fact>]
let ``can override PostRestart methods when starting actor with computation expression`` () =
    
    let postRestartCalled = ref false
    let postRestart = Some(fun (baseFn : exn -> unit) -> postRestartCalled := true)
    
    use system = System.create "testSystem" (Configuration.load())
    let actor = 
        spawnOptOvrd system "actor4" 
        <| actorOf2 (fun mailbox (msg : string) ->
                if msg = "restart" then
                    failwith "System must be restarted"
                else
                    mailbox.Sender() <! msg)
        <| [ SpawnOption.SupervisorStrategy (Strategy.OneForOne (fun error ->
                Directive.Restart)) ]
        <| {defOvrd with PostRestart = postRestart}
    actor <! "restart"
    let response = actor <? "msg" |> Async.RunSynchronously
    system.Shutdown()
    system.AwaitTermination()
    (!postRestartCalled, response) |> equals (true, "msg")