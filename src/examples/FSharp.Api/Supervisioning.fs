module Supervisioning

open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp

type Msg =
    | Value of int
    | Respond

/// worker function
let workerFun (mailbox : Actor<_>) = 
    let state = ref 0 // we store value in a reference cell
    
    let rec loop() = 
        actor { 
            let! msg = mailbox.Receive()
            // worker should save state only for positive integers, and respond on demand, all other options causes exceptions
            match msg with
            | Value num when num > 0 -> 
                state := num
            | Value num ->
                logErrorf mailbox "Received an error-prone value %d" num
                raise (ArithmeticException "values equal or less than 0")
            | Respond -> mailbox.Sender() <! !state
            return! loop()
        }
    loop()

let main() = 
    let system = System.create "SypervisorSystem" <| ConfigurationFactory.Default()
    
    // below we define OneForOneStrategy to handle specific exceptions incoming from child actors
    let strategy = 
        Strategy.OneForOne (fun e -> 
            match e with
            | :? ArithmeticException -> Directive.Resume
            | :? ArgumentException -> Directive.Stop
            | _ -> Directive.Escalate)
    
    // create a supervisor actor
    let supervisor = 
        spawnOpt system "master" (fun mailbox -> 
            // by using spawn we may create a child actors without exiting a F# functional API
            let worker = spawn mailbox "worker" workerFun
            
            let rec loop() = 
                actor { 
                    let! msg = mailbox.Receive()
                    match msg with
                    | Respond -> worker.Tell(msg, mailbox.Sender())
                    | _ -> worker <! msg
                    return! loop()
                }
            loop()) [ SupervisorStrategy(strategy) ]
    
    async { 
        // this one should be handled gently
        supervisor <! Value 5
        do! Async.Sleep 500
        let! r = supervisor <? Respond
        printfn "value received %d" (r :?> int)
    }
    |> Async.RunSynchronously

    async { 
        // this one should cause ArithmeticException in worker actor, handled by it's parent (supervisor)
        // according to SupervisorStrategy, it should resume system work
        supervisor <! Value -11
        do! Async.Sleep 500
        // since -11 thrown an exception and didn't saved a state, a previous value (5) should be returned
        let! r = supervisor <? Respond
        printfn "value received %d" (r :?> int)
    }
    |> Async.RunSynchronously
