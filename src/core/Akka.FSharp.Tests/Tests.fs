//-----------------------------------------------------------------------
// <copyright file="Tests.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

[<AutoOpen>]
module Tests

open Xunit

let equals (expected: 'a) (value: 'a) = Assert.Equal<'a>(expected, value) 
let success = ()
