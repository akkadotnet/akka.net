//-----------------------------------------------------------------------
// <copyright file="InfrastructureTests.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


module Akka.FSharp.Tests.InfrastructureTests

open Akka.FSharp
open Akka.Actor
open System
open Xunit


[<Fact>]
let ``IActorRef should be possible to use as a Key`` () =
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
        Set.empty.Add(aref).Count 
        |> equals 1

[<Fact>]
let ``System.create should support extensions`` () =
    let extensionConfig = 
        """
            akka.actor.provider = cluster
            akka.extensions = ["Akka.Cluster.Tools.Client.ClusterClientReceptionistExtensionProvider, Akka.Cluster.Tools"]
        """
        |> Configuration.parse
    System.create "my-system" extensionConfig
    |> notEquals null
    
