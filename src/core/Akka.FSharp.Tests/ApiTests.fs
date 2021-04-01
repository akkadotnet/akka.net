//-----------------------------------------------------------------------
// <copyright file="ApiTests.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
//
//[<Fact>]
//let ``can serialize and deserialize discriminated unions over remote nodes using wire serializer`` () =     
//    let remoteConfig port = 
//        sprintf """
//        akka { 
//            actor {
//                ask-timeout = 5s
//                provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
//                serialization-bindings {
//                    "System.Object" = wire
//                }
//            }
//            remote {
//                dot-netty.tcp {
//                    port = %i
//                    hostname = localhost
//                }
//            }
//        }
//        """ port
//        |> Configuration.parse
//
//    use server = System.create "server-system" (remoteConfig 9911)
//    use client = System.create "client-system" (remoteConfig 0)
//
//    let aref = 
//        spawne client "a-1" <@ actorOf2 (fun mailbox msg -> 
//               match msg with
//               | C("a-11", B(11, "a-12")) -> mailbox.Sender() <! msg
//               | _ -> mailbox.Unhandled msg) @>
//            [SpawnOption.Deploy (Deploy(RemoteScope (Address.Parse "akka.tcp://server-system@localhost:9911")))]
//    let msg = C("a-11", B(11, "a-12"))
//    let response = aref <? msg |> Async.RunSynchronously
//    response
//    |> equals msg

//[<Fact>]
// FAILS
let ``actor that accepts _ will receive unit message`` () =    
    let timeoutConfig =
        """
        akka { 
            actor {
                ask-timeout = 5s
            }
        }
        """
        |> Configuration.parse 

    let getWhateverHandler (mailbox : Actor<_>) _ = 
        mailbox.Sender() <! "SomethingToReturn"

    let system = System.create "my-system" timeoutConfig
    let aref = spawn system "UnitActor" (actorOf2 getWhateverHandler)

    let response = aref <? () |> Async.RunSynchronously
    response
    |> equals "SomethingToReturn"

[<Fact>]
// SUCCEEDS
let ``actor that accepts _ will receive string message`` () =    
    if (Environment.OSVersion.Platform = PlatformID.Win32NT) then
        let timeoutConfig =
            """
            akka { 
                actor {
                    ask-timeout = 5s
                }
            }
            """
            |> Configuration.parse 

        let getWhateverHandler (mailbox : Actor<_>) _ = 
            mailbox.Sender() <! "SomethingToReturn"

        let system = System.create "my-system" timeoutConfig
        let aref = spawn system "UnitActor" (actorOf2 getWhateverHandler)

        let response = aref <? "SomeRandomInput" |> Async.RunSynchronously
        response
        |> equals "SomethingToReturn"



type TestActor() =
    inherit UntypedActor()

    override x.OnReceive msg = ()

type TestActorWithArgs(arg1, arg2, arg3) =
    inherit UntypedActor()
    do
        ignore (arg1, arg2, arg3)
    
    override x.OnReceive msg = ()


[<Fact>]
let ``can spawn simple actor from expression`` () =
    let system = Configuration.load() |> System.create "test"
    let actor = spawnObj system "test-actor" <@ fun () -> TestActor() @>
    ()

[<Fact>]
let ``can spawn actor with constant args from expression`` () =
    let system = Configuration.load() |> System.create "test"
    let actor = spawnObj system "test-actor" <@ fun () -> TestActorWithArgs(box 1, box true, box "yo") @>
    ()

[<Fact>]
let ``can spawn actor with captured args from expression`` () =
    let system = Configuration.load() |> System.create "test"
    let arg1 = 1
    let arg2 = true
    let arg3 = "yo"
    let actor = spawnObj system "test-actor" <@ fun () -> TestActorWithArgs(box arg1, box arg2, box arg3) @>
    ()

[<Fact>]
let ``cannot spawn actor with simple expr args from expression`` () =
    let system = Configuration.load() |> System.create "test"
    // this formulation is supported in FsApi's expression evaluator, however the checks in Props.Create
    // do not support this, so we test that we can evaluate this but not actually run it, as a proof of concept
    Assert.Throws<InvalidCastException>(fun () ->
        let actor = spawnObj system "test-actor" <@ fun () ->
            let arg1 = 1
            let arg2 = true
            let arg3 = "yo"
            TestActorWithArgs(box arg1, box arg2, box arg3)
        @>
        ()
    )

//[<Fact>]
// FAILS
let ``actor that accepts unit will receive unit message`` () =    
    let timeoutConfig =
        """
        akka { 
            actor {
                ask-timeout = 5s
            }
        }
        """
        |> Configuration.parse 

    let getWhateverHandler (mailbox : Actor<unit>) () = 
        mailbox.Sender() <! "SomethingToReturn"

    let system = System.create "my-system" timeoutConfig
    let aref = spawn system "UnitActor" (actorOf2 getWhateverHandler)

    let response = aref <? () |> Async.RunSynchronously
    response
    |> equals "SomethingToReturn"
