//-----------------------------------------------------------------------
// <copyright file="LruBoundedCacheBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Remote.Serialization;
using Akka.Util;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Akka.Benchmarks.Remoting
{
    [Config(typeof(MicroBenchmarkConfig))]
    [SimpleJob( invocationCount: 10_000_000)]
    public class LruBoundedCacheBenchmarks
    {
        private ActorSystem _sys1;
        private Config _config = @"akka.actor.provider = remote
                                     akka.remote.dot-netty.tcp.port = 0";

        private ActorRefResolveCache _resolveCache;
        private ActorPathCache _pathCache;
        private AddressCache _addressCache;

        private string _cacheMissPath;
        private IActorRef _cacheMissActorRef;

        private string _cacheHitPath;
        private int _cacheHitPathCount = 0;
        private IActorRef _cacheHitActorRef;

        private Address _addr1;
        private string _addr1String;

        [GlobalSetup]
        public async Task Setup()
        {
            _sys1 = ActorSystem.Create("BenchSys", _config);
            _resolveCache = ActorRefResolveThreadLocalCache.For(_sys1).Cache;
            _pathCache = ActorPathThreadLocalCache.For(_sys1).Cache;
            _addressCache = AddressThreadLocalCache.For(_sys1).Cache;

            var es = (ExtendedActorSystem)_sys1;
            _addr1 = es.Provider.DefaultAddress;
            _addr1String = _addr1.ToString();

            var name = "target" + ++_cacheHitPathCount;
            _cacheHitActorRef = _sys1.ActorOf(act =>
            {
                act.ReceiveAny((o, context) => context.Sender.Tell(context.Sender));
            }, name);

            _cacheMissActorRef = await _cacheHitActorRef.Ask<IActorRef>("hit", CancellationToken.None);

            _cacheHitPath = _cacheHitActorRef.Path.ToString();
            _cacheMissPath = _cacheMissActorRef.Path.ToString();
        }

        [IterationSetup]
        public void IterationSetup()
        {
            _cacheMissPath = $"/user/f/{_cacheHitPathCount++}";
            _resolveCache.TrySet(_cacheHitPath, _cacheHitActorRef);
            _addressCache.TrySet(_addr1String, _addr1);
            _pathCache.TrySet(_cacheHitPath, _cacheHitActorRef.Path);
            
            ConsoleLogger.Default.WriteLine(LogKind.Info, $"Cached IActorRef? {_resolveCache.TryGet(_cacheHitPath, out _)}");
            ConsoleLogger.Default.WriteLine(LogKind.Info, $"Cached Address? {_addressCache.TryGet(_addr1String, out _)}");
            ConsoleLogger.Default.WriteLine(LogKind.Info, $"Cached ActorPath? {_pathCache.TryGet(_cacheHitPath, out _)}");
        }

        [Benchmark]
        public void ActorRefResolveMissBenchmark()
        {
            _resolveCache.GetOrCompute("/user/ignore");
        }
        
        [Benchmark]
        public void ActorRefResolveHitBenchmark()
        {
            _resolveCache.GetOrCompute(_cacheHitPath);
        }

        [Benchmark]
        public void AddressHitBenchmark()
        {
            _addressCache.GetOrCompute(_addr1String);
        }
        
        [Benchmark]
        public void ActorPathCacheHitBenchmark()
        {
            _pathCache.GetOrCompute(_cacheHitPath);
        }
        
        [Benchmark]
        public void ActorPathCacheMissBenchmark()
        {
            _pathCache.GetOrCompute(_cacheMissPath);
        }
        
        [GlobalCleanup]
        public async Task Cleanup()
        {
            await _sys1.Terminate();
        }
    }
}
