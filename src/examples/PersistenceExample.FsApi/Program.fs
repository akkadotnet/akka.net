//-----------------------------------------------------------------------
// <copyright file="Program.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

open System
open Akka.FSharp
open Akka.Persistence
open Akka.Persistence.FSharp

    
let system = System.create "sys" (Configuration.load())

// Scenario 0: basic statefull persistent actor
module Scenario0 =

    // general update state method
    let update state e = e::state

    // apply is invoked when actor receives a recovery event
    let apply _ = update

    // exec is invoked when a actor receives a new message from another entity
    let exec (mailbox: Eventsourced<_,_,_>) state cmd = 
        match cmd with
        | "print" -> printf "State is: %A\n" state          // print current actor state
        | s       -> mailbox.PersistEvent (update state) [s]     // persist event and call update state on complete

    let run() =
        printfn "--- SCENARIO 0 ---\n"
        let s0 = 
            spawnPersist system "s0" {  // s0 identifies actor uniquelly across different incarnations
                state = []              // initial state
                apply = apply           // recovering function
                exec = exec             // command handler
            } []    

        s0 <! "foo"
        s0 <! "bar"
        s0 <! "baz"

        s0 <! "print"

// Scenario 1: statefull actor recovering from snapshot
module Scenario1 =

    type Command =
        | Update of string      // update actor's internal state
        | TakeSnapshot          // order actor to save snapshot of it's current state
        | Print                 // print actor's internal state
        | Crash                 // order actor to blow up itself with exception

    let update state e = (e.ToString())::state

    // apply function can recover not only from received events, but also from state snapshot
    let apply (mailbox: Eventsourced<Command,obj,string list>) state (event:obj) = 
        match event with
        | :? string as e -> update state e
        | :? SnapshotOffer as o -> o.Snapshot :?> string list
        | x -> 
            mailbox.Unhandled x
            state

    let exec (mailbox: Eventsourced<Command,obj,string list>) state cmd =
        match cmd with
        | Update s -> mailbox.PersistEvent (update state) [s]
        | TakeSnapshot -> mailbox.SaveSnapshot state
        | Print -> printf "State is: %A\n" state
        | Crash -> failwith "planned crash"

    let run() =
    
        printfn "--- SCENARIO 1 ---\n"
        let s1 = 
            spawnPersist system "s1" {
                state = []
                apply = apply
                exec = exec
            } []

        s1 <! Update "a"
        s1 <! Print
        // restart
        s1 <! Crash
        s1 <! Print
        s1 <! Update "b"
        s1 <! Print
        s1 <! TakeSnapshot
        s1 <! Update "c"
        s1 <! Print
    
Scenario0.run()

Console.ReadLine()

