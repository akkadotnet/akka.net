//-----------------------------------------------------------------------
// <copyright file="Program.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

open Akka.FSharp
open Akka.Actor

// the most basic configuration of remote actor system
let config = """
akka {  
    actor {
        provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
    }    
    remote.dot-netty.tcp {
        transport-protocol = tcp
        port = 0                    # get first available port
        hostname = 0.0.0.0          
    }
}
"""

// create remote deployment configuration for actor system available under `actorPath`
let remoteDeploy systemPath = 
    let address = 
        match ActorPath.TryParseAddress systemPath with
        | false, _ -> failwith "ActorPath address cannot be parsed"
        | true, a -> a
    Deploy(RemoteScope(address))

[<Literal>]
let REQ = 1

[<Literal>]
let RES = 2

[<EntryPoint>]
let main _ = 
    System.Console.Title <- "Local: " + System.Diagnostics.Process.GetCurrentProcess().Id.ToString()
    // remote system address according to settings provided 
    // in FSharp.Deploy.Remote configuration
    let remoteSystemAddress = "akka.tcp://remote-system@localhost:7000"
    use system = System.create "local-system" (Configuration.parse config)
    
    // spawn actor remotelly on remote-system location
    let remoter = 
        // as long as actor receive logic is serializable F# Expr, there is no need for sharing any assemblies 
        // all code will be serialized, deployed to remote system and there compiled and executed
        spawne system "remote" 
            <@ 
                fun mailbox -> 
                let rec loop(): Cont<int * string, unit> = 
                    actor { 
                        let! msg = mailbox.Receive()
                        match msg with
                        | (REQ, m) -> 
                            printfn "Remote actor received: %A" m
                            mailbox.Sender() <! (RES, "ECHO " + m)
                        | _ -> logErrorf mailbox "Received unexpected message: %A" msg
                        return! loop()
                    }
                loop() 
             @> [ SpawnOption.Deploy(remoteDeploy remoteSystemAddress) ]
    async { 
        let! msg = remoter <? (REQ, "hello")
        match msg with
        | (RES, m) -> printfn "Remote actor responded: %s" m
        | _ -> printfn "Unexpected response from remote actor"
    }
    |> Async.RunSynchronously
    System.Console.ReadLine() |> ignore
    0

