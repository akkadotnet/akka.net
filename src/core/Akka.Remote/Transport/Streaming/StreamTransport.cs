using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Remote.Transport.Helios;

namespace Akka.Remote.Transport.Streaming
{
    public class StreamTransportSettings
    {
        public Config Config { get; }

        public int StreamWriteBufferSize { get; }

        public int StreamReadBufferSize { get; }

        public int MaximumFrameSize { get; }

        public int ChunkedReadThreshold { get; }

        public int FrameSizeHardLimit { get; }

        public MessageDispatcher RemoteDispatcher { get; internal set; }

        public TimeSpan FlushWaitTimeout { get; internal set; }

        public ILoggingAdapter Log { get; internal set; }

        public StreamTransportSettings(Config config)
        {
            Config = config;

            StreamWriteBufferSize = GetByteSize(config, "stream-write-buffer-size");
            StreamReadBufferSize = GetByteSize(config, "stream-read-buffer-size");
            MaximumFrameSize = GetByteSize(config, "maximum-frame-size", 32000);
            ChunkedReadThreshold = GetByteSize(config, "chunked-read-threshold");
            FrameSizeHardLimit = GetByteSize(config, "frame-size-hard-limit", 32000);
        }

        internal StreamTransportSettings(HeliosTransportSettings heliosSettings)
        {
            Config = heliosSettings.Config;

            StreamWriteBufferSize = 4096;
            StreamReadBufferSize = 65536;
            MaximumFrameSize = heliosSettings.MaxFrameSize;
            ChunkedReadThreshold = 4096;
            FrameSizeHardLimit = 67108864;
        }

        public virtual bool ShutdownOutput(Stream stream, object state)
        {
            return false;
        }

        public virtual void CloseStream(Stream stream, object state)
        {
            try
            {
                stream.Close();
            }
            catch (Exception ex)
            {
                //TODO Log
                // Weird but not fatal
            }
        }

        protected static int GetByteSize(Config config, string path, int minValue = 0, int maxValue = int.MaxValue)
        {
            long? option = config.GetByteSize(path);

            if (option == null)
                throw new ConfigurationException($"Setting '{path}' is missing.");

            long size = option.Value;
            if (size < minValue)
                throw new ConfigurationException($"Setting '{path}' must be at least '{minValue}'.");

            if (size > maxValue)
                throw new ConfigurationException($"Setting '{path}' must be smaller than '{maxValue}'.");

            return (int)size;
        }
    }

    public abstract class StreamTransport : Transport
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly HashSet<StreamAssociationHandle> _associations = new HashSet<StreamAssociationHandle>();

        public StreamTransportSettings Settings { get; }

        protected Address InboundAddress { get; private set; }

        protected CancellationToken ShutdownToken => _cancellation.Token;

        public override long MaximumPayloadBytes => Settings.MaximumFrameSize;

        protected StreamTransport(ActorSystem system, StreamTransportSettings settings)
        {
            _cancellation = new CancellationTokenSource();

            System = system;
            Config = settings.Config;
            Settings = settings;
            Settings.Log = Logging.GetLogger(System, this);

            var dispatcherId = System.Settings.Config.GetString("akka.remote.use-dispatcher");

            if (dispatcherId == null)
                throw new InvalidOperationException("The setting 'akka.remote.use-dispatcher' is missing from config.");

            if (!System.Dispatchers.HasDispatcher(dispatcherId))
                throw  new InvalidOperationException($"Dispatcher '{dispatcherId}' is missing from config.");

            Settings.RemoteDispatcher = System.Dispatchers.Lookup(dispatcherId);
            Settings.FlushWaitTimeout = System.Settings.Config.GetTimeSpan("akka.remote.flush-wait-on-shutdown");
        }

        public sealed override Task<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>> Listen()
        {
            TaskCompletionSource<IAssociationEventListener> completion = new TaskCompletionSource<IAssociationEventListener>();

            InboundAddress = Initialize();

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
            return true;
        }

        public sealed override async Task<bool> Shutdown()
        {
            _cancellation.Cancel();
            bool gracefulShutdown = await ShutdownAssociations();
            Cleanup();
            return gracefulShutdown;
        }

        public void RegisterAssociation(StreamAssociationHandle association)
        {
            lock (_associations)
            {
                _associations.Add(association);
            }

            association.Stopped.ContinueWith(_ =>
            {
                lock (_associations)
                {
                    _associations.Remove(association);
                }
            }, ShutdownToken);
        }

        private async Task<bool> ShutdownAssociations()
        {
            StreamAssociationHandle[] associations;
            lock (_associations)
            {
                associations = _associations.ToArray();
                _associations.Clear();
            }

            var tasks = associations.Select(a =>
            {
                // The above layer is supposed to have already disassociated all association,
                // but let's play safe and call it here in case it wasn't called.
                a.Disassociate();

                return a.Stopped;
            }).ToArray();

            
            bool[] results;

            using (var cancellation = new CancellationTokenSource((int)Settings.FlushWaitTimeout.TotalMilliseconds * 2))
            {
                results = await Task.WhenAll(tasks).ContinueWith(task => task.Result, cancellation.Token);
            }

            return results.All(flushSucceeded => flushSucceeded);
        }

        protected abstract void Cleanup();

        public override string ToString()
        {
            return GetType().Name;
        }
    }
}