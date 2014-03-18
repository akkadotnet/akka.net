namespace Akka.FSharp.Tests

open Akka.FSharp



[<TestClass>]
type ComputationExpression() =
    let send m =
        function
        | Func f -> f m
        | r -> r

    let value =
        function
        | Return (v:'a) -> v
        | _ -> Microsoft.VisualStudio.TestTools.UnitTesting.Assert.Fail("Expected a value, found a continuation.")
               Unchecked.defaultof<'a>

    [<TestMethod>]
    member x.``return should return value``() =
        actor { return 1}
        |> value
        |> equals 1


    [<TestMethod>]
    member x.``let should bind message``() =
        actor { let! m = IO<int>.Input
                return m } 
        |> send 42
        |> value
        |> equals 42


    [<TestMethod>]
    member x.``let should bind functions``() =
        let f() = actor { let! m = IO<int>.Input
                          return m}
        actor {
            let! x = f()
            let! m = IO<int>.Input
            return x,m }
        |> send 42
        |> send 84
        |> value
        |> equals (42, 84)

    [<TestMethod>]
    member x.``successive bindings can be combined``() =
        actor {
            let! m = IO<int>.Input
            let! n = IO<int>.Input
            return m,n }
        |> send 42
        |> send 84
        |> value
        |> equals (42, 84)

    [<TestMethod>]
    member x.``zero can be used when no ouput is given``() =
        actor {
            let! m = IO<int>.Input
            do () }
        |> send 42 
        |> value
        |> equals ()

    [<TestMethod>]
    member x.``returnfrom can return a whole actor result``() = 
        let rec f n = actor { let! m = IO<int>.Input 
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

    [<TestMethod>]
    member x.``try catch should catch exceptions after getting message``() =
       actor {
            try
                let! m = IO<int>.Input
                failwith "Should stop here !"
                let! n = IO<int>.Input

                return None
            with
            | ex -> return Some ex.Message }
       |> send 0
       |> value
       |> equals (Some "Should stop here !")

    [<TestMethod>]
    member x.``try catch should catch exceptions befor getting message``() =
       actor {
            try
                failwith "Should stop here !"
                let! m = IO<int>.Input
                let! n = IO<int>.Input

                return None
            with
            | ex -> return Some ex.Message }
       |> value
       |> equals (Some "Should stop here !")


    [<TestMethod>]
     member x.``try catch returns body content when no exception occure``() =
       actor {
            try
                let! m = IO<int>.Input
                let! n = IO<int>.Input
                return None
            with
            | ex -> return Some ex.Message }
       |> send 0
       |> send 1
       |> value
       |> equals None

    [<TestMethod>]
    member x.``While should loop only when condition holds``() =
        let cont = ref true
        let count = ref 0
        let r = 
            actor {
                while !cont do
                    let! m = IO<int>.Input
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

    [<TestMethod>]
    member x.``Loops without message input should loop``() =
        let count = ref 0
        actor {
            while !count<10 do
                count := !count + 1

            return !count }
        |> value
        |> equals  10