using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Remote.Transport.Streaming
{
    public abstract class StreamTransport : Transport
    {
        private readonly CancellationTokenSource _cancellation;
        private string _responsibleProtocol;

        protected Address InboundAddress { get; private set; }

        protected CancellationToken CancelToken
        {
            get { return _cancellation.Token; }
        }

        public override long MaximumPayloadBytes
        {
            get { return int.MaxValue; }
        }

        protected StreamTransport(ActorSystem system, Config config)
        {
            _cancellation = new CancellationTokenSource();

            System = system;
            Config = config;
        }

        public sealed override Task<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>> Listen()
        {
            TaskCompletionSource<IAssociationEventListener> completion = new TaskCompletionSource<IAssociationEventListener>();

            InboundAddress = Initialize();

            _responsibleProtocol = "akka." + SchemeIdentifier;

            completion.Task.ContinueWith(task =>
            {
                StartAcceptingConnections(task.Result);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

            return Task.FromResult(Tuple.Create(InboundAddress, completion));
        }

        protected abstract Address Initialize();

        protected abstract void StartAcceptingConnections(IAssociationEventListener listener);

        public override bool IsResponsibleFor(Address remote)
        {
            return string.Equals(_responsibleProtocol, remote.Protocol, StringComparison.OrdinalIgnoreCase);
        }

        public sealed override Task<bool> Shutdown()
        {
            // TODO Close all connections and flush writes?

            _cancellation.Cancel();
            Cleanup();
            return Task.FromResult(true);
        }

        public abstract void Cleanup();
    }
}