[<AutoOpen>]
module Tests

open Microsoft.VisualStudio.TestTools.UnitTesting

type TestClassAttribute = Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute
type TestMethodAttribute = Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute


let equals (expected: 'a) (value: 'a) = Assert.AreEqual(expected, value) 
let success = ()