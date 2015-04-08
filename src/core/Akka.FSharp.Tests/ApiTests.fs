module Akka.FSharp.Tests.ApiTests

open Akka.FSharp
open Akka.Actor
open System
open Xunit


[<Fact>]
let ``configuration loader should load data from app.config`` () =
    let config = Configuration.load()
    config.HasPath "akka.test.value" 
    |> equals true
    config.GetInt "akka.test.value"
    |> equals 10

[<Fact>]
let ``can serialize expression decider`` () =
    let decider = ExprDecider <@ fun e -> Directive.Resume @>
    use sys = System.create "system" (Configuration.defaultConfig())
    let serializer = sys.Serialization.FindSerializerFor decider
    let bytes = serializer.ToBinary decider
    let des = serializer.FromBinary (bytes, typeof<ExprDecider>) :?> IDecider
    des.Decide (Exception())
    |> equals (Directive.Resume)