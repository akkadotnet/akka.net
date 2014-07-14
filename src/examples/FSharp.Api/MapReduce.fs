module MapReduce

open System
open System.Collections.Concurrent
open Akka.Actor
open Akka.Configuration
open Akka.Routing
open Akka.FSharp

/// Collection of messages used for map-reduce implementation
type MRMsg =
    | Collect                           // collect all reduced data so far
    | Reduce of (string * int) list     // reduce mapped data
    | Map of string                     // map chunk of data

/// Helper function used to wrap message handler functions into Akka.NET actor API
let actorFor func' = fun (mailbox:Actor<MRMsg>) ->
    let rec loop() = actor {
        let! msg = mailbox.Receive()
        func' mailbox msg
        return! loop() }
    loop()

let mapWords (line:string) = seq { for word in line.Split() -> (word.ToLower(), 1) }

/// Mapper actor function used for mapping chunk of data onto 
/// specialized data structure and forwarding it to the reducer
let map reducer (mailbox:Actor<MRMsg>) = function
    | Map line  -> reducer <! Reduce (mapWords line |> List.ofSeq)
    | m         -> mailbox.Unhandled m

let reduceWords (dict:ConcurrentDictionary<string,int>) iter = 
    iter
    |> List.iter (fun (k, v) -> dict.AddOrUpdate(k, v, System.Func<_,_,_>(fun key old -> old + v)) |> ignore)

/// Reducer actor function, which either reduces mapped data into shared dictionary
/// or returns all reduced data on demand
let reduce (dict:ConcurrentDictionary<string,int>) (mailbox:Actor<MRMsg>) = function
    | Reduce l  -> reduceWords dict l |> ignore
    | Collect   -> mailbox.Sender() <! seq { for e in dict -> (e.Key, e.Value) }
    | m         -> mailbox.Unhandled m

/// Master actor function, used as proxy between system user and internal Map/Reduce implementation
/// Can either forward data chunk to mappers or forward a request of returning all reduced data
let master mapper (reducer:InternalActorRef) (mailbox:Actor<MRMsg>) = function
    | Map line  -> mapper <! Map line
    | Collect   -> reducer.Tell(Collect, mailbox.Sender())
    | m         -> mailbox.Unhandled m

let main() =
    let system = System.create "MapReduceSystem" <| ConfigurationFactory.Default()

    let dict = ConcurrentDictionary<string,int>()

    // initialize system actors
    let reducer = spawn system "reduce" <| actorFor (reduce dict)
    let mapper =  spawn system "map"    <| actorFor (map reducer)
    let master =  spawn system "master" <| actorFor (master mapper reducer)

    // send input data
    master <! Map "Orange orange apple"
    master <! Map "cherry apple orange"

    Threading.Thread.Sleep 500

    // return processed data and display it
    async {
        let! res = master.Ask(Collect) |> Async.AwaitTask
        for (k, v) in res :?> (string*int) seq do
            printfn "%s\t%d" k v
        system.Shutdown()
    } |> Async.RunSynchronously
