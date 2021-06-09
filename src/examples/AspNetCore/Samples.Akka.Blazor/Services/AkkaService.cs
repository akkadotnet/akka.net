using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Samples.Akka.Blazor.Actors;

namespace Samples.Akka.Blazor.Services
{
    public class AkkaService : IAsyncDisposable, ICounterService
    {
        private readonly ActorSystem _actorSystem;
        private readonly IActorRef _countActor;

        public AkkaService(ActorSystem actorSystem)
        {
            _actorSystem = actorSystem;
            _countActor = actorSystem.ActorOf(CounterActor.Props, "counter");
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
