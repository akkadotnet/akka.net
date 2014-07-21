[<AutoOpen>]
module Tests

open Xunit

let equals (expected: 'a) (value: 'a) = Assert.Equal<'a>(expected, value) 
let success = ()