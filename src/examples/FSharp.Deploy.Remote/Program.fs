//-----------------------------------------------------------------------
// <copyright file="Program.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

open Akka.FSharp

// the most basic configuration of remote actor system
let config = """
akka {  
    actor {
        provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
    }    
    remote.dot-netty.tcp {
        transport-protocol = tcp
        port = 7000                 
        hostname = localhost
    }
}
"""

[<EntryPoint>]
let main _ = 
    System.Console.Title <- "Remote: " + System.Diagnostics.Process.GetCurrentProcess().Id.ToString()
    // remote system only listens for incoming connections
    // it will receive actor creation request from local-system (see: FSharp.Deploy.Local)
    use system = System.create "remote-system" (Configuration.parse config)
    System.Console.ReadLine() |> ignore
    0

