﻿//-----------------------------------------------------------------------
// <copyright file="ApiTests.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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

type TestUnion = 
    | A of string
    | B of int * string

type TestUnion2 = 
    | C of string * TestUnion
    | D of int


[<Fact>]
let ``can serialize discriminated unions`` () =
    let x = B (23,"hello")
    use sys = System.create "system" (Configuration.defaultConfig())
    let serializer = sys.Serialization.FindSerializerFor x
    let bytes = serializer.ToBinary x
    let des = serializer.FromBinary (bytes, typeof<TestUnion>) :?> TestUnion
    des
    |> equals x

[<Fact>]
let ``can serialize nested discriminated unions`` () =
    let x = C("bar",B (23,"hello"))
    use sys = System.create "system" (Configuration.defaultConfig())
    let serializer = sys.Serialization.FindSerializerFor x
    let bytes = serializer.ToBinary x
    let des = serializer.FromBinary (bytes, typeof<TestUnion2>) :?> TestUnion2
    des
    |> equals x