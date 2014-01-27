module Pigeon.FSharp
open Pigeon.Actor

[<AbstractClass>]
type Actor()=
    inherit Pigeon.Actor.UntypedActor()

let (<!) (actorRef:Pigeon.Actor.ActorRef) (msg: System.Object) =
        actorRef.Tell msg
        ignore()

//Ignore this for now... 