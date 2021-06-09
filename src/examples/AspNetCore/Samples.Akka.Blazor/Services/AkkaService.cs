using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Samples.Akka.Blazor.Actors;

namespace Samples.Akka.Blazor.Services
{
    public class AkkaService : IAsyncDisposable, ICounterService
    {
        private readonly ActorSystem _actorSystem;
        private readonly IActorRef _countActor;

        public static readonly Config Config = @"
        akka.actor.default-dispatcher = {
            executor = channel-executor
            fork-join-executor { #channelexecutor will re-use these settings
              parallelism-min = 2
              parallelism-factor = 1
              parallelism-max = 64
            }
        }

        akka.actor.scheduler.implementation = ""Akka.Actor.TimerScheduler""

        akka.actor.internal-dispatcher = {
            executor = channel-executor
            throughput = 5
            fork-join-executor {
              parallelism-min = 4
              parallelism-factor = 1.0
              parallelism-max = 64
            }
        }

        akka.remote.default-remote-dispatcher {
            type = Dispatcher
            executor = channel-executor
            fork-join-executor {
              parallelism-min = 2
              parallelism-factor = 0.5
              parallelism-max = 16
            }
        }

        akka.remote.backoff-remote-dispatcher {
          executor = channel-executor
          fork-join-executor {
            parallelism-min = 2
            parallelism-max = 2
          }
        }
        ";

        public AkkaService()
        {
            _actorSystem = ActorSystem.Create("Blazor", Config);
            _countActor = _actorSystem.ActorOf(CounterActor.Props, "counter");
        }

        public async ValueTask DisposeAsync()
        {
            await _actorSystem.Terminate();
        }

        public async Task<int> GetCount(CancellationToken token)
        {
            return await _countActor.Ask<int>(CounterActor.Get.Instance, token).ConfigureAwait(false);
        }

        public async Task<int> Increment(CancellationToken token)
        {
            return await _countActor.Ask<int>(CounterActor.Hit.Instance, token).ConfigureAwait(false);
        }
    }
}
