module Pigeon.FSharp
open Pigeon.Actor

[<AbstractClass>]
type Actor()=
    inherit Pigeon.Actor.UntypedActor()

let (<!) (actorRef:ActorRef) (msg: obj) =
    actorRef.Tell msg
    ignore()

let (<?) (tell:ICanTell) (msg: obj) =
    tell.Ask msg
    |> Async.AwaitIAsyncResult 
    |> Async.Ignore

