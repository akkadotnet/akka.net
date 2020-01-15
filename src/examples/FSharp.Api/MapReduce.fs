//-----------------------------------------------------------------------
// <copyright file="MapReduce.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

module MapReduce

open System
open System.Collections.Concurrent
open Akka.Actor
open Akka.Configuration
open Akka.FSharp

/// Collection of messages used for map-reduce implementation
type MRMsg =
    | Collect                           // collect all reduced data so far
    | Reduce of (string * int) list     // reduce mapped data
    | Map of string                     // map chunk of data

let mapWords (line: string) = [ for word in line.Split() -> (word.ToLower(), 1) ]

/// Mapper actor function used for mapping chunk of data onto 
/// specialized data structure and forwarding it to the reducer
let map reducer (mailbox: Actor<MRMsg>) = function
    | Map line  -> reducer <! Reduce (mapWords line)
    | m         -> mailbox.Unhandled m

let reduceWords (dict: ConcurrentDictionary<string, int>) iter = 
    iter
    |> List.iter (fun (k, v) -> dict.AddOrUpdate(k, v, fun _ old -> old + v) |> ignore)

/// Reducer actor function, which either reduces mapped data into shared dictionary
/// or returns all reduced data on demand
let reduce (dict:ConcurrentDictionary<string, int>) (mailbox: Actor<MRMsg>) = function
    | Reduce l  -> reduceWords dict l |> ignore
    | Collect   -> mailbox.Sender() <! seq { for e in dict -> (e.Key, e.Value) }
    | m         -> mailbox.Unhandled m

/// Master actor function, used as proxy between system user and internal Map/Reduce implementation
/// Can either forward data chunk to mappers or forward a request of returning all reduced data
let master mapper (reducer:IActorRef) (mailbox: Actor<MRMsg>) = function
    | Map line  -> mapper <! Map line
    | Collect   -> reducer.Forward Collect
    | m         -> mailbox.Unhandled m

let main() =
    let system = System.create "MapReduceSystem" <| ConfigurationFactory.Default()
    let dict = ConcurrentDictionary<string,int>()

    // initialize system actors
    let reducer = spawn system "reduce" <| actorOf2 (reduce dict)
    let mapper =  spawn system "map"    <| actorOf2 (map reducer)
    let master =  spawn system "master" <| actorOf2 (master mapper reducer)

    // send input data
    master <! Map "Orange orange apple"
    master <! Map "cherry apple orange"

    Threading.Thread.Sleep 500

    // return processed data and display it
    async {
        let! res = master <? Collect
        for (k, v) in res do
            printfn "%s\t%d" k v
        system.Terminate().Wait()
    } |> Async.RunSynchronously

