
//-----------------------------------------------------------------------
// <copyright file="InfrastructureTests.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

module Akka.FSharp.Tests.InfrastructureTests

open Akka.FSharp
open Akka.Actor
open System
open Xunit


[<Fact>]
let ``IActorRef should be possible to use as a Key`` () =
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
    Set.empty.Add(aref).Count 
    |> equals 1