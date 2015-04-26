//-----------------------------------------------------------------------
// <copyright file="Program.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    
    // exec is invoked when a actor receives a new message from another entity
    let exec (mailbox: Eventsourced<_,_,_>) state cmd = 
        match cmd with
        | "print" -> printf "State is: %A\n" state          // print current actor state
        | s       -> mailbox.PersistEvent (update state) [s]     // persist event and call update state on complete

    let run() =
        printfn "--- SCENARIO 0 ---\n"
        let s0 = 
            spawnPersist system "p0" [] // pass empty list as initial state
            <| fun aggregate ->
                { apply = update; 
                  exec = 
                    let rec loop () =
                        actor {
                            let! msg = aggregate.Receive()
                            let state = aggregate.State()
                            match msg with
                            | "print" -> printfn "%A\n" state
                            | m -> aggregate.PersistEvent (update state) [m]
                            return! loop ()
                        }
                    loop () }
            <| []

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
    
    let run() =
    
        printfn "--- SCENARIO 1 ---\n"
        let s1 = 
            spawnPersist system "p0" 
            <| []   // initial state
            <| fun aggregate ->
                { apply = 
                    fun state (event: obj) ->
                        match event with
                        | :? SnapshotOffer as o -> o.Snapshot :?> string list
                        | :? string as e -> update state e
                        | x -> 
                            aggregate.Unhandled x
                            state
                  exec = 
                    let rec loop () =
                        actor {
                            let! cmd = aggregate.Receive()
                            let state = aggregate.State()                            
                            match cmd with
                            | Update s -> aggregate.PersistEvent (update state) [s]
                            | TakeSnapshot -> aggregate.SaveSnapshot state
                            | Print -> printf "State is: %A\n" state
                            | Crash -> failwith "planned crash"
                            return! loop ()
                        }
                    loop () }
            <| []   // spawn options

        s1 <! Update "a"            // state (1-st run): [a]
        s1 <! Print                 
        // restart
        s1 <! Crash                 // throw an exception (planned crash)
        s1 <! Print
        s1 <! Update "b"            // state (1-st run): [b;a]
        s1 <! Print
        s1 <! TakeSnapshot          // store current state on the file system - it persists program finish
        // "c" won't be persisted in in-memory journal scenario after program finishes
        s1 <! Update "c"            // state (1-st run): [c;b;a] 
        s1 <! Print
    
Scenario1.run()

Console.ReadLine()

