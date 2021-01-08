//-----------------------------------------------------------------------
// <copyright file="Supervisioning.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

module Supervisioning

open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp

type CustomException() =
    inherit Exception()

type Message =
    | Echo of string
    | Crash

let main() =
    use system = System.create "system" (Configuration.defaultConfig())
    // create parent actor to watch over jobs delegated to it's child
    let parent = 
        spawnOpt system "parent" 
            <| fun parentMailbox ->
                // define child actor
                let child = 
                    spawn parentMailbox "child" <| fun childMailbox ->
                        childMailbox.Defer (fun () -> printfn "Child stopping")
                        printfn "Child started"
                        let rec childLoop() = 
                            actor {
                                let! msg = childMailbox.Receive()
                                match msg with
                                | Echo info -> 
                                    // respond to original sender
                                    let response = "Child " + (childMailbox.Self.Path.ToStringWithAddress()) + " received: "  + info
                                    childMailbox.Sender() <! response
                                | Crash -> 
                                    // log crash request and crash
                                    printfn "Child %A received crash order" (childMailbox.Self.Path)
                                    raise (CustomException())
                                return! childLoop()
                            }
                        childLoop()
                // define parent behavior
                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive()
                        child.Forward(msg)  // forward all messages through
                        return! parentLoop()
                    }
                parentLoop()
            // define supervision strategy
            <| [ SpawnOption.SupervisorStrategy (
                    // restart on Custom Exception, default behavior on all other exception types
                    Strategy.OneForOne(fun e ->
                    match e with
                    | :? CustomException -> Directive.Restart 
                    | _ -> SupervisorStrategy.DefaultDecider.Decide(e)))  ]

    async {
        let! response = parent <? Echo "hello world"
        printfn "%s" response
        // after this one child should crash
        parent <! Crash
        System.Threading.Thread.Sleep 200
        
        // actor should be restarted
        let! response = parent <? Echo "hello world2"
        printfn "%s" response
    } |> Async.RunSynchronously

