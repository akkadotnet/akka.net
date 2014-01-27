// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.
open Pigeon.Actor
open Pigeon.FSharp
open System

type SomeActorMessages =
    | Greet of string
    | Hi

type SomeActor() =
    inherit Pigeon.Actor.UntypedActor()

    override x.OnReceive message =
        match message with
        | :? SomeActorMessages as m ->  
            match m with
            | Greet(name) -> Console.WriteLine("Hello {0}",name)
            | Hi -> Console.WriteLine("Hello from F#!")
        | _ -> failwith "unknown message"

let system = ActorSystem.Create("FSharpActors")
let actor = system.ActorOf<SomeActor>("MyActor")

actor <! Greet "roger"
actor <! Hi

System.Console.ReadKey() |> ignore
