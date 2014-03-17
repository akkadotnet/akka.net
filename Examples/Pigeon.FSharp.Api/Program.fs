open Akka.Actor
open Akka.FSharp
open Akka.Configuration
open System

type SomeActorMessages =
    | Greet of string
    | Hi

type SomeActor() =    
    inherit Actor()

    override x.OnReceive message =
        match message with
        | :? SomeActorMessages as m ->  
            match m with
            | Greet(name) -> Console.WriteLine("Hello {0}",name)
            | Hi -> Console.WriteLine("Hello from F#!")
        | _ -> failwith "unknown message"

let system =  
    ConfigurationFactory.Default() 
    |> System.create "FSharpActors"

let actor = 
    spawn system "MyActor"
    <| fun recv ->
        let rec again name =
            actor {
                let! message = recv
                match message with
                | Greet(n) when n = name ->
                    printfn "Hello again, %s" name
                    return! again name
                | Greet(n) -> 
                    printfn "Hello %s" n
                    return! again n
                | Hi -> 
                    printfn "Hello from F#!"
                    return! again name }
        and loop() =
            actor {
                let! message = recv
                match message with
                | Greet(name) -> 
                    printfn "Hello %s" name
                    return! again name
                | Hi ->
                    printfn "Hello from F#!"
                    return! loop() } 
        loop()

actor <! Greet "roger"
actor <! Hi
actor <! Greet "roger"
actor <! Hi
actor <! Greet "jeremie"
actor <! Hi
actor <! Greet "jeremie"

System.Console.ReadKey() |> ignore
