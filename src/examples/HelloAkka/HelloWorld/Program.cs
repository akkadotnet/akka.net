//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using Akka.Actor;
using HelloWorld;

// ReSharper disable SuggestVarOrType_SimpleTypes
#region akka-hello-world-main
ActorSystem system = ActorSystem.Create("the-universe");

// create actor and get a reference to it.
// this will be an "ActorRef", which is not a 
// reference to the actual actor instance
// but rather a client or proxy to it
IActorRef greeter = system.ActorOf<GreetingActor>("greeter");

// send a message to the actor
greeter.Tell(new Greet("World"));

// give the actor a moment to process the message
// (it should process it in under a micro-second, but it happens asynchronously)
await Task.Delay(TimeSpan.FromSeconds(1));
system.Stop(greeter);

await system.Terminate();
#endregion
