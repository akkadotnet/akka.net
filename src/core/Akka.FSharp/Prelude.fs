//-----------------------------------------------------------------------
// <copyright file="Prelude.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
module internal Akka.FSharp.OptionHelper

open System

let toNullable = 
    function 
    | Some x -> Nullable x
    | None -> Nullable()
