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

