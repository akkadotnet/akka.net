module Akka.FSharp.Tests.RemoteSpecs
open Akka.Actor
open Xunit
open Xunit.Abstractions
open Akka.FSharp
open Akka.Remote
open Akka.TestKit

let remoteConfig port = 
        $"""
        akka {{ 
            actor {{
                ask-timeout = 5s
                provider = remote
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

type RemoteSpecs(output:ITestOutputHelper) as this =
    inherit AkkaSpec((remoteConfig 0), output)
    
    /// Retrieve the bound address used by Sys
    member this.GetAddress =
        getAddress this.Sys
        
    [<Fact>]
    member _.``can serialize and deserialize discriminated unions over remote nodes using default serializer`` () =
        
        // arrange
        use clientSys = System.create "clientSys" (remoteConfig 0)
        let addr2 = getAddress clientSys
        
        // act
        let aref = 
            spawne clientSys "a-1" <@ actorOf2 (fun mailbox msg -> 
                   match msg with
                   | C("a-11", B(11, "a-12")) -> mailbox.Sender() <! msg
                   | _ -> mailbox.Unhandled msg) @>
                [SpawnOption.Deploy (Akka.Actor.Deploy(RemoteScope(this.GetAddress)))]
                
        let msg = C("a-11", B(11, "a-12"))
        aref.Tell(msg, this.TestActor)
         
        // assert
        this.ExpectMsg(msg)
        
        

