//-----------------------------------------------------------------------
// <copyright file="ComputationExpression.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

module Akka.FSharp.Tests.ComputationExpression

open Akka.FSharp
open System
open Xunit

let send m =
    function
    | Func f -> f m
    | r -> r

let value =
    function 
    | Return (v:'a) -> v
    | _ -> failwith "Expected a value, found a continuation."

[<Fact>]
let ``return should return value``() =
    actor { return 1}
    |> value
    |> equals 1

[<Fact>]
let ``let should bind message``() =
    actor { let! m = IO<int>.Input
            return m } 
    |> send 42
    |> value
    |> equals 42

[<Fact>]
let ``let should bind functions``() =
    let f() = 
        actor { let! m = IO<int>.Input
                return m}
    actor {
        let! x = f()
        let! m = IO<int>.Input
        return x,m }
    |> send 42
    |> send 84
    |> value
    |> equals (42, 84)

[<Fact>]
let ``successive bindings can be combined``() =
    actor {
        let! m = IO<int>.Input
        let! n = IO<int>.Input
        return m,n }
    |> send 42
    |> send 84
    |> value
    |> equals (42, 84)

[<Fact>]
let ``zero can be used when no ouput is given``() =
    actor {
        let! _ = IO<int>.Input
        do () }
    |> send 42 
    |> value
    |> equals ()

[<Fact>]
let ``returnfrom can return a whole actor result``() = 
    let rec f n = 
        actor { let! m = IO<int>.Input 
                match m with
                | 42 -> return n
                | _ -> return! f (n+1) }
    actor {
        return! f 1
    }
    |> send 0
    |> send 54
    |> send 42
    |> value
    |> equals 3

[<Fact>]
let ``try catch should catch exceptions after getting message``() =
    actor {
        try
            let! _ = IO<int>.Input
            failwith "Should stop here !"
            let! _ = IO<int>.Input

            return None
        with
        | ex -> return Some ex.Message }
    |> send 0
    |> value
    |> equals (Some "Should stop here !")

[<Fact>]
let ``try catch should catch exceptions befor getting message``() =
    actor {
        try
            failwith "Should stop here !"
            let! _ = IO<int>.Input
            let! _ = IO<int>.Input

            return None
        with
        | ex -> return Some ex.Message }
    |> value
    |> equals (Some "Should stop here !")

[<Fact>]
let ``try catch returns body content when no exception occure``() =
    actor {
        try
            let! _ = IO<int>.Input
            let! _ = IO<int>.Input
            return None
        with
        | ex -> return Some ex.Message }
    |> send 0
    |> send 1
    |> value
    |> equals None

[<Fact>]
let ``While should loop only when condition holds``() =
    let cont = ref true
    let count = ref 0
    let r = 
        actor {
            while !cont do
                let! _ = IO<int>.Input
                count := !count + 1

            return !count
        }
        |> send 1
        |> send 2
    cont := false 
    r
    |> send 3
    |> value
    |> equals 3

[<Fact>]
let ``Loops without message input should loop``() =
    let count = ref 0
    actor {
        while !count<10 do
            count := !count + 1

        return !count }
    |> value
    |> equals  10

[<Fact>]
let ``finally should be executed when an exception occures before first message``() =
    let finallyCalled = ref false
    let result =
        try
            actor {
                try
                    failwith "exception"
                    let! m = IO<int>.Input
                    return m
                finally
                    finallyCalled := true
            } 
            |> value
            |> Choice1Of2
        with
        | ex -> Choice2Of2 ex.Message

    (!finallyCalled, result) |> equals (true, Choice2Of2 "exception")

[<Fact>]
let ``finally should be executed when an exception occures before after message``() =
    let finallyCalled = ref false
    let result =
        try
            actor {
                try
                    let! m = IO<int>.Input
                    let! _ = IO<int>.Input

                    failwith "exception"

                    return m
                finally
                    finallyCalled := true
            }
            |> send 1
            |> send 2
            |> value
            |> Choice1Of2
        with
        | ex -> Choice2Of2 ex.Message

    (!finallyCalled, result) |> equals (true, Choice2Of2 "exception")

[<Fact>]
let ``finally should be executed when no exception occures``() =
    let finallyCalled = ref false
    let result =
        try
            actor {
                try
                    let! m = IO<int>.Input
                    let! n = IO<int>.Input
                    return m,n
                finally
                    finallyCalled := true
            }
            |> send 1
            |> send 2
            |> value
            |> Choice1Of2
        with
        | ex -> Choice2Of2 ex.Message

    (!finallyCalled, result) |> equals (true, Choice1Of2 (1,2))

[<Fact>]
let ``use should be disposed when an exception occures before before message``() =
    let disposeCalled = ref false
    let result =
        try
            actor {
                use _ = { new IDisposable with member __.Dispose() = disposeCalled := true }
                failwith "exception"
                let! m = IO<int>.Input
                let! _ = IO<int>.Input

                return m }
            |> value
            |> Choice1Of2
        with
        | ex -> Choice2Of2 ex.Message

    (!disposeCalled, result) |> equals (true, Choice2Of2 "exception")

[<Fact>]
let ``use should be disposed when an exception occures before after message``() =
    let disposeCalled = ref false
    let result =
        try
            actor {
                use _ = { new IDisposable with member __.Dispose() = disposeCalled := true }
                let! m = IO<int>.Input
                let! _ = IO<int>.Input

                failwith "exception"
                return m }
            |> send 1
            |> send 2
            |> value
            |> Choice1Of2
        with
        | ex -> Choice2Of2 ex.Message

    (!disposeCalled, result) |> equals (true, Choice2Of2 "exception")

[<Fact>]
let ``use should be disposed when no exception occures``() =
    let disposeCalled = ref false
    let result =
        try
            actor {
                use _ = { new IDisposable with member __.Dispose() = disposeCalled := true}
                let! m = IO<int>.Input
                let! n = IO<int>.Input
                return m,n
            }
            |> send 1
            |> send 2
            |> value
            |> Choice1Of2
        with
        | ex -> Choice2Of2 ex.Message

    (!disposeCalled, result) |> equals (true, Choice1Of2 (1,2))

[<Fact>]
let ``for should loop message handler``() =
    actor {
        let total = ref 0
        for _ in 1 .. 3 do
            let! m = IO<int>.Input 
            total := !total + m
        return !total }
    |> send 1
    |> send 2
    |> send 3
    |> value
    |> equals 6

[<Fact>]
let ``for should loop with no message handler``() =
    actor {
        let total = ref 0
        for i in [1 .. 3] do
            total := !total + i
        return !total }
    |> value
    |> equals 6

[<Fact>]
let ``for should do nothing when source is empty``() =
    actor {
        let total = ref 0
        for i in [] do
            let! _ = IO<int>.Input 
            total := !total + i
        return !total }
    |> value
    |> equals 0

