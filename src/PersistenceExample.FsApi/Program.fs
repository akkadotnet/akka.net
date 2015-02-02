open System
open Akka.FSharp
open Akka.Persistence
open Akka.Persistence.FSharp

    
let system = System.create "sys" (Configuration.load())

// Scenario 0: basic statefull persistent actor
module Scenario0 =

    let update state e = e::state
    let apply _ = update
    let exec (mailbox: Eventsourced<_,_,_>) state cmd = 
        match cmd with
        | "print" -> printf "State is: %A\n" state
        | s       -> mailbox.Persist [s] (update state)
        state

    let run() =
        printfn "--- SCENARIO 0 ---\n"
        let s0 = 
            spawnPersist system "s0" {
                state = []
                apply = apply
                exec = exec
            } []

        s0 <! "foo"
        s0 <! "bar"
        s0 <! "baz"

        s0 <! "print"

// Scenario 1: statefull actor recovering from snapshot
module Scenario1 =

    type Command =
        | Update of string
        | TakeSnapshot
        | Print
        | Crash

    let update state e = (e.ToString())::state
    let apply (mailbox: Eventsourced<Command,obj,string list>) state (event:obj) = 
        match event with
        | :? string as e -> update state e
        | :? SnapshotOffer as o -> o.Snapshot :?> string list
        | x -> 
            mailbox.Unhandled x
            state
    let exec (mailbox: Eventsourced<Command,obj,string list>) state cmd =
        match cmd with
        | Update s -> mailbox.Persist [s] (update state)
        | TakeSnapshot -> mailbox.SaveSnapshot state
        | Print -> printf "State is: %A\n" state
        | Crash -> failwith "planned crash"
        state

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

        // expected output at the first run:
        // State is: [a]
        // Error crash report
        // State is: [a]
        // State is: [b;a]
        // State is: [c;b;a]
        
        // expected output at the second run:
        // State is: [a;b;a]
        // Error crash report
        // State is: [a;b;a]
        // State is: [b;a;b;a]
        // State is: [c;b;a;b;a]

    
Scenario1.run()

Console.ReadLine()