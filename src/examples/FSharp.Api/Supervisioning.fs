module Supervisioning

open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp

/// Define message type for immediate value return
type Respond = Respond

/// worker function
let workerFun (mailbox : Actor<'msg>) = 
    let state = ref 0 // we store value in a reference cell
    
    let rec loop() = 
        actor { 
            let! msg = mailbox.Receive()
            // worker should save state only for positive integers, and respond on demand, all other options causes exceptions
            match msg :> obj with
            | :? int as num -> 
                if num > 0 then state := num
                else
                    logError mailbox (sprintf "Received an error-prone value %d" num)
                    raise (ArithmeticException "values equal or less than 0")
            | :? Respond -> mailbox.Sender() <! !state
            | _ -> raise (ArgumentException "Unsupported argument")
            return! loop()
        }
    loop()

let main() = 
    let system = System.create "SypervisorSystem" <| ConfigurationFactory.Default()
    
    // create a supervisor actor
    let supervisor = 
        spawnp system "master" // below we define OneForOneStrategy to handle specific exceptions incoming from child actors
                               <| fun p -> 
            { p with SupervisorStrategy = 
                         Some(Strategy.oneForOne (fun e -> 
                                  match e with
                                  | :? ArithmeticException -> Directive.Resume
                                  | :? ArgumentException -> Directive.Stop
                                  | _ -> Directive.Escalate)) }
        <| fun mailbox -> 
            // by using spawn we may create a child actors without exiting a F# functional API
            let worker = spawn mailbox "worker" <| workerFun
            
            let rec loop() = 
                actor { 
                    let! msg = mailbox.Receive()
                    match msg :> obj with
                    | :? Respond -> worker.Tell(msg, mailbox.Sender())
                    | _ -> worker <! msg
                    return! loop()
                }
            loop()
    async { 
        // this one should be handled gently
        supervisor <! 5
        System.Threading.Thread.Sleep 500
        let! r = supervisor <? Respond
        printfn "value received %d" (r :?> int)
    }
    |> Async.RunSynchronously
    async { 
        // this one should cause ArithmeticException in worker actor, handled by it's parent (supervisor)
        // according to SupervisorStrategy, it should resume system work
        supervisor <! -11
        System.Threading.Thread.Sleep 500
        // since -11 thrown an exception and didn't saved a state, a previous value (5) should be returned
        let! r = supervisor <? Respond
        printfn "value received %d" (r :?> int)
    }
    |> Async.RunSynchronously
