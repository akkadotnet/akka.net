open Akka.FSharp
open Akka.Actor

// the most basic configuration of remote actor system
let config = """
akka {  
    actor {
        provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
    }    
    remote.helios.tcp {
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
        | (false, _) -> failwith "ActorPath address cannot be parsed"
        | (true, a) -> a
    Deploy(RemoteScope(address))

[<EntryPoint>]
let main argv = 
    System.Console.Title <- "Local: " + System.Diagnostics.Process.GetCurrentProcess().Id.ToString()
    // remote system address according to settings provided 
    // in FSharp.Deploy.Remote configuration
    let remoteSystemAddress = "akka.tcp://remote-system@localhost:7000"
    use system = System.create "local-system" (Configuration.parse config)

    // spawn actor remotelly on remote-system location
    let remoter = 
        spawne system "remote" 
        // define deployment configuration using remote system address
        <| fun p -> { p with Deploy = Some (remoteDeploy remoteSystemAddress) }
        // as long as actor receive logic is serializable F# Expr, there is no need for sharing any assemblies 
        // all code will be serialized, deployed to remote system and there compiled and executed
        <| <@ fun mailbox -> 
            let rec loop'() : Cont<string, string> = actor {
                let! msg = mailbox.Receive()
                printfn "Remote actor received: %A" msg
                return! loop'()
            }
            loop'() @>

    remoter <! "hello"

    System.Console.ReadLine()
    0 // return an integer exit code
