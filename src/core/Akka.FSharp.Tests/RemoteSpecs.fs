module Akka.FSharp.Tests.RemoteSpecs

open System
open Akka.Actor
open Akka.Configuration
open Akka.Serialization
open Newtonsoft.Json.Converters
open Xunit
open Xunit.Abstractions
open Akka.FSharp
open Akka.Remote
open Akka.TestKit
open Serialization

let remoteConfig port = 
        $"""
        akka {{ 
            actor {{
                ask-timeout = 5s
                provider = remote
                serializers {{
                  hyperion = "Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion"
                }}
                serialization-bindings {{
                  "System.Object" = hyperion
                }}
            }}
            remote {{
                dot-netty.tcp {{
                    port = %i{port}
                    hostname = localhost
                }}
            }}
        }}
        """
        |> Configuration.parse

let getAddress (actorSystem:ActorSystem) =
    let es = actorSystem :?> ExtendedActorSystem
    es.Provider.DefaultAddress
    
type TestUnion = 
    | A of string
    | B of int * string

type TestUnion2 = 
    | C of string * TestUnion
    | D of int
    
type Record1 = {Name:string;Age:int;}

type Msg =
    | R of Record1

type RemoteSpecs(output:ITestOutputHelper) as this =
    inherit AkkaSpec((remoteConfig 0), output)
    
    do exprSerializationSupport this.Sys
    
    /// Retrieve the bound address used by Sys
    member this.GetAddress =
        getAddress this.Sys
        
    [<Fact>]
    member _.``can serialize and deserialize F# discriminated unions over remote nodes using Hyperion serializer`` () =
        
        // arrange
        use clientSys = System.create "clientSys" (remoteConfig 0)
        let addr2 = getAddress clientSys
        this.InitializeLogger(clientSys) // setup XUnit output tracking in client system
        
        // act
        let aref = 
            spawne clientSys "a-1" <@ actorOf2 (fun mailbox msg -> 
                   match msg with
                   | C("a-11", B(11, "a-12")) -> mailbox.Sender() <! msg
                   | _ -> mailbox.Unhandled msg) @>
                [SpawnOption.Deploy Deploy.None]
        
        let msg = C("a-11", B(11, "a-12"))
        
        let selection = this.Sys.ActorSelection(new RootActorPath(addr2) / "user" / "a-1")
        let remoteRef =
            async {
                let! rRef = selection.ResolveOne this.RemainingOrDefault |> Async.AwaitTask
                return rRef
             } |> Async.RunSynchronously       
        
        remoteRef.Tell(msg, this.TestActor)
         
        // assert
        this.ExpectMsg(msg) |> ignore
        
    [<Fact>]
    member _.``can serialize and deserialize F# records over remote nodes using Hyperion serializer`` () =
        
        // arrange
        use clientSys = System.create "clientSys" (remoteConfig 0)
        let addr2 = getAddress clientSys
        
        // act
        let aref = 
            spawne clientSys "a-1" <@ actorOf2 (fun mailbox (msg:obj) -> 
                   match msg with
                   | :? Record1 as r -> mailbox.Log.Value.Info("Received message {0}", r)
                                        mailbox.Sender() <! r
                   | _ ->  mailbox.Unhandled msg) @>
                [SpawnOption.Deploy(Deploy.None)]
        
        let msg = { Name = "aaron"; Age = 30 }
        
        let selection = this.Sys.ActorSelection(new RootActorPath(addr2) / "user" / "a-1")
        let remoteRef =
            async {
                let! rRef = selection.ResolveOne this.RemainingOrDefault |> Async.AwaitTask
                return rRef
             } |> Async.RunSynchronously       
        
        remoteRef.Tell(msg, this.TestActor)
        
        // assert
        this.ExpectMsg(msg) |> ignore
       