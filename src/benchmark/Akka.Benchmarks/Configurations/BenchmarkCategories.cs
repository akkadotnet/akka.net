// -----------------------------------------------------------------------
//  <copyright file="BenchmarkCategories.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.Benchmarks.Configurations;

public static class BenchmarkCategories
{
    public const string MacroBenchmark = "MacroBenchmark";
    public const string MicroBenchmark = "MicroBenchmark";
    
    public const string AkkaIOBenchmark = "Akka.IO";
    public const string DispatcherBenchmark = "Akka.Dispatch";
    public const string ActorSpawningBenchmark = "Actor-Spawning";
    public const string ActorMessagingBenchmark = "Actor-Messaging";
    public const string AkkaActorBenchmark = "Akka.Actor";
    public const string AkkaEventBenchmark = "Akka.Event";
    
    public const string HoconBenchmark = "Akka.Configuration";
}