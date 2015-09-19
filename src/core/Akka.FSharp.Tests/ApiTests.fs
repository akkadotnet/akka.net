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

