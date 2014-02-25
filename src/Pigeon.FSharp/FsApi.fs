module Akka.FSharp
open Akka.Actor

[<AbstractClass>]
type Actor()=
    inherit Akka.Actor.UntypedActor()

let (<!) (actorRef:ActorRef) (msg: obj) =
    actorRef.Tell msg
    ignore()

let (<?) (tell:ICanTell) (msg: obj) =
    tell.Ask msg
    |> Async.AwaitIAsyncResult 
    |> Async.Ignore

