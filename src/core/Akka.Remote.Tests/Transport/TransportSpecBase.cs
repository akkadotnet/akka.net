using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions.Execution;
using Google.ProtocolBuffers;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    using Akka.Remote.Transport;
    using System.Collections.Generic;
    public abstract class TransportSpecBase : AkkaSpec
    {
        [ThreadStatic]
        protected static Random t_random;

        protected static Random Random
        {
            get
            {
                if (t_random == null)
                    t_random = new Random();

                return t_random;
            }
        }

        private readonly Config _baseConfig;

        public TransportHook Transport1 { get; }
        public TransportHook Transport2 { get; }

        public AssociationHook Association1 { get; }
        public AssociationHook Association2 { get; }

        protected TransportSpecBase(string transportId, string baseTransportConfig, string baseConfig = null)
            :base(baseConfig)
        {
            _baseConfig = ConfigurationFactory.ParseString(baseTransportConfig)
                .SafeWithFallback(GetDefaultTransportConfig(transportId));

            Transport1 = CreateTransport();
            Transport2 = CreateTransport();

            Transport1.Listen();
            Transport2.Listen();

            Association1 = Transport1.Connect(Transport2);
            Association2 = Transport2.ExpectInboundAssociation(Association1);
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                Transport1.Shutdown();
                Transport2.Shutdown();
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        protected TransportHook CreateTransport(string config)
        {
            return CreateTransport(ConfigurationFactory.ParseString(config));
        }

        protected TransportHook CreateTransport(Config config = null)
        {
            config = config.SafeWithFallback(_baseConfig);

            string transportClass = config.GetString("transport-class");

            return new TransportHook(CreateTransport(transportClass, Sys, config));
        }

        public static Config GetDefaultTransportConfig(string transportId)
        {
            var defaultConfig = GetDefaultRemoteConfig();

            var config = defaultConfig.GetConfig("akka.remote." + transportId);

            if (config == null)
                throw new ArgumentException("Invalid Transport id.", nameof(transportId));

            return config;
        }

        public static Config GetDefaultRemoteConfig()
        {
            var assembly = typeof(RemoteActorRefProvider).Assembly;

            using (var stream = assembly.GetManifestResourceStream("Akka.Remote.Configuration.Remote.conf"))
            {
                Debug.Assert(stream != null, "stream != null");
                using (var reader = new StreamReader(stream))
                {
                    var result = reader.ReadToEnd();

                    return ConfigurationFactory.ParseString(result);
                }
            }
        }

        private static Transport CreateTransport(string transportClass, ActorSystem system, Config config)
        {
            var driverType = Type.GetType(transportClass);
            if (driverType == null)
                throw new TypeLoadException(string.Format("Cannot instantiate transport [{0}]. Cannot find the type.", transportClass));

            if (!typeof(Transport).IsAssignableFrom(driverType))
                throw new TypeLoadException(string.Format("Cannot instantiate transport [{0}]. It does not implement [{1}].", transportClass, nameof(Transport)));

            var constructorInfo = driverType.GetConstructor(new[] { typeof(ActorSystem), typeof(Config) });
            if (constructorInfo == null)
                throw new TypeLoadException(string.Format("Cannot instantiate transport [{0}]. " +
                                                          "It has no public constructor with " +
                                                          "[{1}] and [{2}] parameters", transportClass, nameof(ActorSystem), nameof(Config)));

            return (Transport)Activator.CreateInstance(driverType, system, config);
        }

        public Task RunRandomSession(int approximateMessageCount)
        {
            if (Random.Next(0, 2) == 0)
                return RunRandomSession(Transport1, Transport2, approximateMessageCount);

            return RunRandomSession(Transport2, Transport1, approximateMessageCount);
        }

        public async Task RunRandomSession(TransportHook outboundTransport, TransportHook inboundTransport, int approximateMessageCount)
        {
            var ass1 = await outboundTransport.ConnectAsync(inboundTransport).ConfigureAwait(false);
            var ass2 = await inboundTransport.ExpectInboundAssociationAsync(ass1).ConfigureAwait(false);

            int messageCount1 = Random.Next(1, approximateMessageCount * 2);
            int messageCount2 = Random.Next(1, approximateMessageCount * 2);

            var task1 = Task.Run(() => RunRandomWrites(ass1, ass2, messageCount1));
            var task2 = Task.Run(() => RunRandomWrites(ass2, ass1, messageCount2));

            // It's normal that one run will fail since the first who finish disassociate the connection
            List<Exception> exceptions = new List<Exception>();
            try
            {
                await task1.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }

            try
            {
                await task2.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }


            if (exceptions.Count == 2)
                throw new AggregateException("Random session test failed.", exceptions);
        }

        protected Task RunRandomWrites(AssociationHook writer, AssociationHook reader, int messageCount)
        {
            const int minMessageSize = 1;
            const int maxMessageSize = 991;
            int seed = Random.Next();

            CancellationTokenSource cancellation = new CancellationTokenSource();

            var writerTask = Task.Run(async () =>
            {
                try
                {
                    var random = new Random(seed);
                    for (int i = 0; i < messageCount; i++)
                    {
                        await Task.Yield();

                        if (cancellation.Token.IsCancellationRequested)
                            break;

                        int length = random.Next(minMessageSize, maxMessageSize + 1);

                        var buffer = CreateBuffer(length);
                        writer.Write(buffer);
                    }

                    writer.Disassociate();
                }
                catch (Exception)
                {
                    cancellation.Cancel();
                    throw;
                }

            });

            var readerTask = Task.Run(async () =>
            {
                try
                {
                    var random = new Random(seed);
                    for (int i = 0; i < messageCount; i++)
                    {
                        int expectedLength = random.Next(minMessageSize, maxMessageSize + 1);
                        var actual = await reader.ExpectPayloadAsync();
                        ValidateBuffer(actual, expectedLength);
                    }
                }
                catch (Exception)
                {
                    cancellation.Cancel();
                    throw;
                }

            });

            return Task.WhenAll(writerTask, readerTask);
        }

        protected static byte[] CreateBuffer(int length)
        {
            byte[] buffer = new byte[length];

            for (int i = 0; i < length; i++)
                buffer[i] = unchecked((byte)i);

            return buffer;
        }

        protected static void ValidateBuffer(byte[] buffer, int expectedLength)
        {
            if (buffer.Length != expectedLength)
                throw new AssertionFailedException($"Expected length {expectedLength} and got {buffer.Length}");

            for (int i = 0; i < buffer.Length; i++)
            {
                byte expected = unchecked((byte)i);
                if (buffer[i] != expected)
                    throw new AssertionFailedException($"At index '{i}', Expected {expected} and got {buffer[i]}");
            }
        }
    }

    public class AssociationHook : IHandleEventListener
    {
        private readonly AssociationHandle _association;
        private readonly AsyncQueue<IHandleEvent> _receivedEvents = new AsyncQueue<IHandleEvent>();

        public Address LocalAddress => _association.LocalAddress;
        public Address RemoteAddress => _association.RemoteAddress;

        public AssociationHook(AssociationHandle association)
        {
            _association = association;
        }

        void IHandleEventListener.Notify(IHandleEvent evt)
        {
            if (evt is Disassociated)
                Disassociate();

            if (!(evt is UnderlyingTransportError))
                _receivedEvents.Enqueue(evt);
        }

        public IHandleEvent ExpectEvent(TimeSpan? timeout = null)
        {
            return ExpectEventAsync(timeout).Result;
        }

        public Task<IHandleEvent> ExpectEventAsync(TimeSpan? timeout = null)
        {
            return _receivedEvents.DequeueAsync(timeout ?? TimeSpan.FromSeconds(10));
        }

        public byte[] ExpectPayload(TimeSpan? timeout = null)
        {
            return ExpectPayloadAsync(timeout).Result;
        }

        public async Task<byte[]> ExpectPayloadAsync(TimeSpan? timeout = null)
        {
            var evt = await ExpectEventAsync(timeout).ConfigureAwait(false);

            if (!(evt is InboundPayload))
                throw new AssertionFailedException($"Expected 'InboundPayload' event and got '{evt.GetType().Name}'");

            var payload = (InboundPayload)evt;
            return payload.Payload.ToByteArray();
        }

        public void ExpectPayload(byte[] expected, TimeSpan? timeout = null)
        {
            ExpectPayloadAsync(expected, timeout).Wait();
        }

        public async Task ExpectPayloadAsync(byte[] expected, TimeSpan? timeout = null)
        {
            var evt = await ExpectEventAsync(timeout).ConfigureAwait(false);

            if (!(evt is InboundPayload))
                throw new AssertionFailedException($"Expected 'InboundPayload' event and got '{evt.GetType().Name}'");

            var actual = ExpectPayload();

            if (actual.Length != expected.Length)
                throw new AssertionFailedException($"Expected length {expected.Length} and got {actual.Length}");

            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                    throw new AssertionFailedException($"At index '{i}', Expected {expected[i]} and got {actual[i]}");
            }
        }

        public void ExpectPayload(string expected, TimeSpan? timeout = null)
        {
            ExpectPayloadAsync(expected, timeout).Wait();
        }

        public async Task ExpectPayloadAsync(string expected, TimeSpan? timeout = null)
        {
            byte[] data = await ExpectPayloadAsync(timeout).ConfigureAwait(false);

            Assert.Equal(expected, Encoding.UTF8.GetString(data));
        }

        public void ExpectDisassociated(TimeSpan? timeout = null)
        {
            ExpectDisassociatedAsync(timeout).Wait();
        }
        public async Task ExpectDisassociatedAsync(TimeSpan? timeout = null)
        {
            var evt = await ExpectEventAsync(timeout).ConfigureAwait(false);

            if (!(evt is Disassociated))
                throw new AssertionFailedException($"Expected 'Disassociated' event and got '{evt.GetType().Name}'");
        }

        public void Write(byte[] data)
        {
            _association.Write(ByteString.CopyFrom(data));
        }

        public void Write(string s)
        {
            Write(Encoding.UTF8.GetBytes(s));
        }

        public void Disassociate()
        {
            _association.Disassociate();
        }
    }

    public class TransportHook : IAssociationEventListener
    {
        private readonly Transport _transport;
        private readonly Dictionary<int, object> _pendingExpectDisassociate = new Dictionary<int, object>(); 

        public Address InboundAddress { get; private set; }

        public TransportHook(Transport transport)
        {
            _transport = transport;
        }

        public void Listen()
        {
            var listen = _transport.Listen().ContinueWith(task =>
            {
                InboundAddress = task.Result.Item1;
                var completion = task.Result.Item2;

                completion.TrySetResult(this);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            if (!listen.Wait(1000))
                throw new TimeoutException("Unable to start listening.");
        }

        public void Shutdown(TimeSpan? timeout = null)
        {
            var task = _transport.Shutdown();

            if (!task.Wait(timeout ?? TimeSpan.FromSeconds(10)))
                throw new TimeoutException($"Could not shutdown transport in '{timeout}'");

            Assert.True(task.Result);
        }

        public AssociationHook ExpectInboundAssociation(AssociationHook remoteAssociation, TimeSpan? timeout = null)
        {
            return ExpectInboundAssociationAsync(remoteAssociation, timeout).Result;
        }

        public Task<AssociationHook> ExpectInboundAssociationAsync(AssociationHook remoteAssociation, TimeSpan? timeout = null)
        {
            int port = remoteAssociation.LocalAddress.Port.Value;

            var completion = new TaskCompletionSource<AssociationHook>();
            lock (_pendingExpectDisassociate)
            {
                object item;
                if (_pendingExpectDisassociate.TryGetValue(port, out item))
                {
                    completion.SetResult((AssociationHook)item);
                    return completion.Task;
                }

                _pendingExpectDisassociate.Add(port, completion);
            }

            CancellationTokenSource cancel = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
            return completion.Task.WithCancellation(cancel.Token).ContinueWith(task =>
            {
                cancel.Dispose();

                lock (_pendingExpectDisassociate)
                    _pendingExpectDisassociate.Remove(port);

                return task.Result;

            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        public AssociationHook Connect(TransportHook transport, TimeSpan? timeout = null)
        {
            return ConnectAsync(transport.InboundAddress, timeout).Result;
        }

        public Task<AssociationHook> ConnectAsync(TransportHook transport, TimeSpan? timeout = null)
        {
            return ConnectAsync(transport.InboundAddress, timeout);
        }

        public AssociationHook Connect(Address remoteAddress, TimeSpan? timeout = null)
        {
            return ConnectAsync(remoteAddress, timeout).Result;
        }

        public async Task<AssociationHook> ConnectAsync(Address remoteAddress, TimeSpan? timeout = null)
        {
            timeout = timeout ?? TimeSpan.FromSeconds(10);

            var task = _transport.Associate(remoteAddress);

            var completedTask = await Task.WhenAny(task, Task.Delay(timeout.Value)).ConfigureAwait(false);

            if (!ReferenceEquals(task, completedTask))
                throw new TimeoutException($"Could not connect to address '{remoteAddress}' in '{timeout}'" );

            var association = await task.ConfigureAwait(false);
            var hook =  new AssociationHook(association);
            association.ReadHandlerSource.TrySetResult(hook);
            return hook;
        }

        void IAssociationEventListener.Notify(IAssociationEvent evt)
        {
            InboundAssociation inbound = evt as InboundAssociation;

            if (inbound != null)
            {
                var associationHook = new AssociationHook(inbound.Association);
                inbound.Association.ReadHandlerSource.TrySetResult(associationHook);

                int port = associationHook.RemoteAddress.Port.Value;

                TaskCompletionSource<AssociationHook> completion = null;
                lock (_pendingExpectDisassociate)
                {
                    object item;
                    if (!_pendingExpectDisassociate.TryGetValue(port, out item))
                        _pendingExpectDisassociate.Add(port, associationHook);

                    else
                        completion = (TaskCompletionSource<AssociationHook>)item;
                }

                completion?.TrySetResult(associationHook);
            }
        }
    }

    internal class AsyncQueue<T> : IDisposable
    {
        private readonly Queue<T> _queue;
        private readonly SemaphoreSlim _semaphore;

        public AsyncQueue()
        {
            _queue = new Queue<T>();
            _semaphore = new SemaphoreSlim(0);
        }

        /// <summary>
        /// Add an item to the queue. Can be called concurrently.
        /// </summary>
        /// <returns>True if the item was enqueued, otherwise False.</returns>
        public void Enqueue(T item)
        {
            lock (_queue)
            {
                _queue.Enqueue(item);
            }
            _semaphore.Release();
        }

        /// <summary>
        /// Dequeue an item from the queue. The queue is single consumer, DequeueAsync must not be called again until the previous dequeue operation have completed.
        /// Warning: The returned IFutureItem must not be awaited more than once.
        /// </summary>
        /// <returns>A future of the dequeued item.</returns>
        public async Task<T> DequeueAsync(TimeSpan timeout)
        {
            while (true)
            {
                lock (_queue)
                {
                    if (_queue.Count > 0)
                        return _queue.Dequeue();
                }

                if (!await _semaphore.WaitAsync(timeout).ConfigureAwait(false))
                    throw new TimeoutException("DequeueAsync timeout expired.");
            }
        }

        public void Dispose()
        {
            _semaphore.Dispose();
        }
    }
}