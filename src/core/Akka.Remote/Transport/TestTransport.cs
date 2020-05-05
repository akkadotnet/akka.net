//-----------------------------------------------------------------------
// <copyright file="TestTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util;
using Google.Protobuf;

namespace Akka.Remote.Transport
{
    /// <summary>
    ///     Transport implementation used for testing.
    ///     The TestTransport is basically shared memory between actor systems. It can be programmed to emulate
    ///     different failure modes of a <see cref="Transport" /> implementation. TestTransport keeps a log of the activities
    ///     it was requested to do. This class is not optimized for performance and MUST not be used in production systems.
    /// </summary>
    public class TestTransport : Transport
    {
        private readonly TaskCompletionSource<IAssociationEventListener> _associationListenerPromise =
            new TaskCompletionSource<IAssociationEventListener>();

        private readonly AssociationRegistry _registry;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly SwitchableLoggedBehavior<Address, AssociationHandle> AssociateBehavior;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly SwitchableLoggedBehavior<TestAssociationHandle, bool> DisassociateBehavior;
        /*
         * Programmable behaviors
         */

        /// <summary>
        /// TBD
        /// </summary>
        public readonly SwitchableLoggedBehavior<bool, (Address, TaskCompletionSource<IAssociationEventListener>)>
            ListenBehavior;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Address LocalAddress;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly SwitchableLoggedBehavior<bool, bool> ShutdownBehavior;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly SwitchableLoggedBehavior<(TestAssociationHandle, ByteString), bool> WriteBehavior;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="conf">TBD</param>
        public TestTransport(ActorSystem system, Config conf)
            : this(
                Address.Parse(GetConfigString(conf, "local-address")),
                AssociationRegistry.Get(GetConfigString(conf, "registry-key")),
                conf.GetByteSize("maximum-payload-bytes", null) ?? 32000L,
                GetConfigString(conf, "scheme-identifier")
                )
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="localAddress">TBD</param>
        /// <param name="registry">TBD</param>
        /// <param name="maximumPayloadBytes">TBD</param>
        /// <param name="schemeIdentifier">TBD</param>
        public TestTransport(Address localAddress, AssociationRegistry registry, long maximumPayloadBytes = 32000,
            string schemeIdentifier = "test")
        {
            LocalAddress = localAddress;
            _registry = registry;
            MaximumPayloadBytes = maximumPayloadBytes;
            SchemeIdentifier = schemeIdentifier;
            ListenBehavior =
                new SwitchableLoggedBehavior<bool, (Address, TaskCompletionSource<IAssociationEventListener>)>(
                    x => DefaultListen(), x => _registry.LogActivity(new ListenAttempt(LocalAddress)));
            AssociateBehavior =
                new SwitchableLoggedBehavior<Address, AssociationHandle>(DefaultAssociate,
                    address => registry.LogActivity(new AssociateAttempt(LocalAddress, address)));
            ShutdownBehavior = new SwitchableLoggedBehavior<bool, bool>(x => DefaultShutdown(),
                x => registry.LogActivity(new ShutdownAttempt(LocalAddress)));
            DisassociateBehavior = new SwitchableLoggedBehavior<TestAssociationHandle, bool>(DefaultDisassociate, remote => _registry.LogActivity(new DisassociateAttempt(remote.LocalAddress, remote.RemoteAddress)));

            WriteBehavior = new SwitchableLoggedBehavior<(TestAssociationHandle, ByteString), bool>(
                args => DefaultWriteBehavior(args.Item1, args.Item2),
                data =>
                    _registry.LogActivity(new WriteAttempt(data.Item1.LocalAddress, data.Item1.RemoteAddress, data.Item2)));
        }

        private static string GetConfigString(Config conf, string name)
        {
            var value = conf.GetString(name, null);
            if (value == null)
                throw new ConfigurationException("Please specify a value for config setting \"" + name + "\"");
            return value;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remote">TBD</param>
        /// <returns>TBD</returns>
        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }

        #region Listener methods

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Task<(Address, TaskCompletionSource<IAssociationEventListener>)> Listen()
        {
            return ListenBehavior.Apply(true);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public Task<(Address, TaskCompletionSource<IAssociationEventListener>)> DefaultListen()
        {
            var promise = _associationListenerPromise;
            _registry.RegisterTransport(this, promise.Task);
            return
                Task.FromResult((LocalAddress, promise));
        }

        #endregion

        #region Association methods

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <returns>TBD</returns>
        public override Task<AssociationHandle> Associate(Address remoteAddress)
        {
            return AssociateBehavior.Apply(remoteAddress);
        }

        private async Task<AssociationHandle> DefaultAssociate(Address remoteAddress)
        {
            var transport = _registry.TransportFor(remoteAddress);
            if (transport.HasValue)
            {
                var remoteAssociationListenerTask = transport.Value.Item2;
                var handlers = CreateHandlePair(transport.Value.Item1, remoteAddress);
                var localHandle = handlers.Item1;
                var remoteHandle = handlers.Item2;
                localHandle.Writeable = false;
                remoteHandle.Writeable = false;

                //pass a non-writeable handle to remote first
                var remoteAssociationListener = await remoteAssociationListenerTask.ConfigureAwait(false);
                remoteAssociationListener.Notify(new InboundAssociation(remoteHandle));
                var remoteHandlerTask = remoteHandle.ReadHandlerSource.Task;

                //registration of reader at local finishes the registration and enables communication
                var remoteListener = await remoteHandlerTask.ConfigureAwait(false);

#pragma warning disable 4014
                localHandle.ReadHandlerSource.Task.ContinueWith(result =>
#pragma warning restore 4014
                {
                    var localListener = result.Result;
                    _registry.RegisterListenerPair(localHandle.Key, (localListener, remoteListener));
                    localHandle.Writeable = true;
                    remoteHandle.Writeable = true;
                }, TaskContinuationOptions.ExecuteSynchronously);

                return (AssociationHandle) localHandle;
            }

            throw new InvalidAssociationException($"No registered transport: {remoteAddress}");
        }

        private (TestAssociationHandle, TestAssociationHandle) CreateHandlePair(TestTransport remoteTransport,
            Address remoteAddress)
        {
            var localHandle = new TestAssociationHandle(LocalAddress, remoteAddress, this, false);
            var remoteHandle = new TestAssociationHandle(remoteAddress, LocalAddress, remoteTransport, true);

            return (localHandle, remoteHandle);
        }

        #endregion

        #region Disassociation methods

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handle">TBD</param>
        /// <returns>TBD</returns>
        public Task Disassociate(TestAssociationHandle handle)
        {
            return DisassociateBehavior.Apply(handle);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handle">TBD</param>
        /// <returns>TBD</returns>
        public Task<bool> DefaultDisassociate(TestAssociationHandle handle)
        {
            var handlers = _registry.DeregisterAssociation(handle.Key);
            if (handlers.HasValue)
            {
                handlers.Value.Item1.Notify(new Disassociated(DisassociateInfo.Unknown));
                handlers.Value.Item2.Notify(new Disassociated(DisassociateInfo.Unknown));
            }

            return Task.FromResult(true);
        }

        #endregion

        #region Shutdown methods

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Task<bool> Shutdown()
        {
            return ShutdownBehavior.Apply(true);
        }

        private Task<bool> DefaultShutdown()
        {
            return Task.FromResult(true);
        }

        #endregion

        #region Write methods

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handle">TBD</param>
        /// <param name="payload">TBD</param>
        /// <returns>TBD</returns>
        public Task<bool> Write(TestAssociationHandle handle, ByteString payload)
        {
            return WriteBehavior.Apply((handle, payload));
        }

        private Task<bool> DefaultWriteBehavior(TestAssociationHandle handle, ByteString payload)
        {
            var remoteReadHandler = _registry.GetRemoteReadHandlerFor(handle);

            if (remoteReadHandler != null)
            {
                remoteReadHandler.Notify(new InboundPayload(payload));
                return Task.FromResult(true);
            }

            return Task.Run(() =>
            {
                throw new ArgumentException("No association present");
#pragma warning disable 162
                return true;
#pragma warning restore 162
            });
        }

        #endregion
    }

    /// <summary>
    ///     Base trait for remote activities that are logged by <see cref="TestTransport" />
    /// </summary>
    public abstract class Activity
    {
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class ListenAttempt : Activity
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="boundAddress">TBD</param>
        public ListenAttempt(Address boundAddress)
        {
            BoundAddress = boundAddress;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address BoundAddress { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class AssociateAttempt : Activity
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="localAddress">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        public AssociateAttempt(Address localAddress, Address remoteAddress)
        {
            RemoteAddress = remoteAddress;
            LocalAddress = localAddress;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address LocalAddress { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Address RemoteAddress { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class ShutdownAttempt : Activity
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="boundAddress">TBD</param>
        public ShutdownAttempt(Address boundAddress)
        {
            BoundAddress = boundAddress;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address BoundAddress { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class WriteAttempt : Activity
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="sender">TBD</param>
        /// <param name="recipient">TBD</param>
        /// <param name="payload">TBD</param>
        public WriteAttempt(Address sender, Address recipient, ByteString payload)
        {
            Payload = payload;
            Recipient = recipient;
            Sender = sender;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address Sender { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Address Recipient { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public ByteString Payload { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class DisassociateAttempt : Activity
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="requestor">TBD</param>
        /// <param name="remote">TBD</param>
        public DisassociateAttempt(Address requestor, Address remote)
        {
            Remote = remote;
            Requestor = requestor;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address Requestor { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Address Remote { get; private set; }
    }

    /// <summary>
    ///     Test utility to make behavior of functions that return some Task controllable form tests.
    ///     This tool is able to override default behavior with any generic behavior, including failure, and exposes
    ///     control to the timing of completion of the associated Task.
    ///     The utility is implemented as a stack of behaviors, where the behavior on the top of the stack represents the
    ///     currently active behavior. The bottom of the stack always contains the <see cref="DefaultBehavior" /> which
    ///     can not be popped out.
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class SwitchableLoggedBehavior<TIn, TOut>
    {
        private readonly ConcurrentStack<Func<TIn, Task<TOut>>> _behaviorStack =
            new ConcurrentStack<Func<TIn, Task<TOut>>>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="defaultBehavior">TBD</param>
        /// <param name="logCallback">TBD</param>
        public SwitchableLoggedBehavior(Func<TIn, Task<TOut>> defaultBehavior, Action<TIn> logCallback)
        {
            LogCallback = logCallback;
            DefaultBehavior = defaultBehavior;
            _behaviorStack.Push(DefaultBehavior);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Func<TIn, Task<TOut>> DefaultBehavior { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public Action<TIn> LogCallback { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public Func<TIn, Task<TOut>> CurrentBehavior
        {
            get
            {
                Func<TIn, Task<TOut>> behavior;
                if (_behaviorStack.TryPeek(out behavior))
                    return behavior;
                return DefaultBehavior; //otherwise, return the default behavior
            }
        }

        /// <summary>
        ///     Changes the current behavior to the provided one
        /// </summary>
        /// <param name="behavior">
        ///     Function that takes a parameter type <typeparamref name="TIn" /> and returns a Task
        ///     <typeparamref name="TOut" />.
        /// </param>
        public void Push(Func<TIn, Task<TOut>> behavior)
        {
            _behaviorStack.Push(behavior);
        }

        /// <summary>
        ///     Changes the behavior to return a completed Task with the given constant value.
        /// </summary>
        /// <param name="result">The constant the Task will be completed with.</param>
        public void PushConstant(TOut result)
        {
            Push(x => Task.FromResult(result));
        }

        /// <summary>
        ///     Changes the behavior to return a faulted Task with the given exception
        /// </summary>
        /// <param name="e">The exception responsible for faulting this task</param>
        public void PushError(Exception e)
        {
            Push(x => Task.Run(() =>
            {
                throw e;
#pragma warning disable 162
                return default(TOut);
#pragma warning restore 162
            }));
        }

        /// <summary>
        ///     Enables control of the completion of the previously active behavior. Wraps the previous behavior in
        /// </summary>
        /// <returns>TBD</returns>
        public TaskCompletionSource<bool> PushDelayed()
        {
            var controlPromise = new TaskCompletionSource<bool>();
            var originalBehavior = CurrentBehavior;
            Push(x =>
            {
                controlPromise.Task.Wait();
                return originalBehavior.Invoke(x);
            });

            return controlPromise;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Pop()
        {
            if (_behaviorStack.Count > 1)
            {
                Func<TIn, Task<TOut>> behavior;
                _behaviorStack.TryPop(out behavior);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="param">TBD</param>
        /// <returns>TBD</returns>
        public Task<TOut> Apply(TIn param)
        {
            LogCallback(param);
            return CurrentBehavior(param);
        }
    }

    /// <summary>
    ///     Shared state among <see cref="TestTransport" /> instances. Coordinates the transports and the means of
    ///     communication between them.
    /// </summary>
    /// <remarks>
    ///     NOTE: This is a global shared state between different actor systems. The purpose of this class is to allow
    ///     dynamically
    ///     loaded TestTransports to set up a shared AssociationRegistry.Extensions could not be used for this purpose, as the
    ///     injection
    ///     of the shared instance must happen during the startup time of the actor system. Association registries are looked
    ///     up via a string key. Until we find a better way to inject an AssociationRegistry to multiple actor systems it is
    ///     strongly recommended to use long, randomly generated strings to key the registry to avoid interference between
    ///     tests.
    /// </remarks>
    public class AssociationRegistry
    {
        private static readonly ConcurrentDictionary<string, AssociationRegistry> registries =
            new ConcurrentDictionary<string, AssociationRegistry>();

        private readonly ConcurrentStack<Activity> _activityLog = new ConcurrentStack<Activity>();

        private readonly
            ConcurrentDictionary<(Address, Address), (IHandleEventListener, IHandleEventListener)>
            _listenersTable =
                new ConcurrentDictionary<(Address, Address), (IHandleEventListener, IHandleEventListener)>();

        private readonly ConcurrentDictionary<Address, (TestTransport, Task<IAssociationEventListener>)>
            _transportTable = new ConcurrentDictionary<Address, (TestTransport, Task<IAssociationEventListener>)>();

        /// <summary>
        /// Retrieves the specified <see cref="AssociationRegistry"/> associated with the <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The registry key - see the HOCON example for details.</param>
        /// <returns>An existing or new <see cref="AssociationRegistry"/> instance.</returns>
        /// <code>
        ///     akka{
        ///         remote{
        ///             enabled-transports = ["akka.remote.test"]
        ///             test{
        ///                 registry-key = "SOME KEY"
        ///             }
        ///         }
        ///     }
        /// </code>
        public static AssociationRegistry Get(string key)
        {
            return registries.GetOrAdd(key, new AssociationRegistry());
        }

        /// <summary>
        /// Wipes out all of the <see cref="AssociationRegistry"/> instances retained by this process.
        /// </summary>
        public static void Clear()
        {
            registries.Clear();
        }

        /// <summary>
        ///     Returns the remote endpoint for a pair of endpoints relative to the owner of the supplied
        ///     <see cref="TestAssociationHandle" />.
        /// </summary>
        /// <param name="handle">The reference handle to determine the remote endpoint relative to</param>
        /// <param name="listenerPair">pair of listeners in initiator, receiver order</param>
        /// <returns>TBD</returns>
        public IHandleEventListener RemoteListenerRelativeTo(TestAssociationHandle handle,
            (IHandleEventListener, IHandleEventListener) listenerPair)
        {
            if (handle.Inbound)
                return listenerPair.Item1; //initiator
            return listenerPair.Item2; //receiver
        }

        /// <summary>
        ///     Logs a transport activity
        /// </summary>
        /// <param name="activity">The activity to be logged</param>
        public void LogActivity(Activity activity)
        {
            _activityLog.Push(activity);
        }

        /// <summary>
        ///     Gets a snapshot of the current transport activity log
        /// </summary>
        /// <returns>A IList of activities ordered left-to-right in chronological order (element[0] is the oldest)</returns>
        public IList<Activity> LogSnapshot()
        {
            return _activityLog.Reverse().ToList();
        }

        /// <summary>
        ///     Clears the current contents of the log
        /// </summary>
        public void ClearLog()
        {
            _activityLog.Clear();
        }

        /// <summary>
        ///     Records a mapping between an address and the corresponding (transport, associationEventListener) pair.
        /// </summary>
        /// <param name="transport">The transport that is to be registered. The address of this transport will be used as a key.</param>
        /// <param name="associationEventListenerTask">
        ///     The Task that will be completed with the listener that will handle the
        ///     events for the given transport.
        /// </param>
        public void RegisterTransport(TestTransport transport,
            Task<IAssociationEventListener> associationEventListenerTask)
        {
            _transportTable.TryAdd(transport.LocalAddress, (transport, associationEventListenerTask));
        }

        /// <summary>
        ///     Indicates if all given transports were successfully registered. No associations can be established between
        ///     transports that are not yet registered.
        /// </summary>
        /// <param name="addresses">The listen addresses of transports that participate in the test case.</param>
        /// <returns>True if all transports are successfully registered.</returns>
        public bool TransportsReady(params Address[] addresses)
        {
            return addresses.All(x => _transportTable.ContainsKey(x));
        }

        /// <summary>
        ///     Registers two event listeners corresponding to the two endpoints of an association.
        /// </summary>
        /// <param name="key">
        ///     Ordered pair of addresses representing an association. First element must be the address of the
        ///     initiator.
        /// </param>
        /// <param name="listeners">
        ///     A pair of listeners that will be responsible for handling the events of the two endpoints
        ///     of the association. Elements in the Tuple must be in the same order as the addresses in <paramref name="key" />.
        /// </param>
        public void RegisterListenerPair((Address, Address) key,
            (IHandleEventListener, IHandleEventListener) listeners)
        {
            _listenersTable.AddOrUpdate(key, x => listeners, (x, y) => listeners);
        }

        /// <summary>
        ///     Removes an association.
        /// </summary>
        /// <param name="key">
        ///     Ordered pair of addresses representing an association. First element must be the address of the
        ///     initiator.
        /// </param>
        /// <returns>The original entries, or Option.None if the key wasn't found in the table.</returns>
        public Option<(IHandleEventListener, IHandleEventListener)> DeregisterAssociation((Address, Address) key)
        {
            if (!_listenersTable.TryRemove(key, out var listeners))
                return Option<(IHandleEventListener, IHandleEventListener)>.None;
            
            return listeners;
        }

        /// <summary>
        ///     Tests if an association was registered.
        /// </summary>
        /// <param name="initiatorAddress">The initiator of the association.</param>
        /// <param name="remoteAddress">The other address of the association.</param>
        /// <returns>True if there is an association for the given address.</returns>
        public bool ExistsAssociation(Address initiatorAddress, Address remoteAddress)
        {
            return _listenersTable.ContainsKey((initiatorAddress, remoteAddress));
        }

        /// <summary>
        ///     Returns the event handler corresponding to the remote endpoint of the given local handle. In other words
        ///     it returns the listener that will receive <see cref="InboundPayload" /> events when
        ///     <seealso cref="AssociationHandle.Write" /> is called.
        /// </summary>
        /// <param name="localHandle">The handle</param>
        /// <returns>The option that contains the listener if it exists.</returns>
        public IHandleEventListener GetRemoteReadHandlerFor(TestAssociationHandle localHandle)
        {
            if (_listenersTable.TryGetValue(localHandle.Key, out var listeners))
                return RemoteListenerRelativeTo(localHandle, listeners);

            return null;
        }

        /// <summary>
        ///     Returns the transport bound to the given address.
        /// </summary>
        /// <param name="address">The address bound to the transport.</param>
        /// <returns>The transport, if it exists.</returns>
        public Option<(TestTransport, Task<IAssociationEventListener>)> TransportFor(Address address)
        {
            if (!_transportTable.TryGetValue(address, out var transport))
                return Option<(TestTransport, Task<IAssociationEventListener>)>.None;
            
            return transport;
        }

        /// <summary>
        ///     Clears the state of the entire registry.
        ///     <remarks>
        ///         This method is not atomic and does not use a critical section when clearing transports, listeners, and logs.
        ///     </remarks>
        /// </summary>
        public void Reset()
        {
            ClearLog();
            _transportTable.Clear();
            _listenersTable.Clear();
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class TestAssociationHandle : AssociationHandle
    {
        private readonly TestTransport _transport;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly bool Inbound;
        /// <summary>
        /// TBD
        /// </summary>
        internal volatile bool Writeable = true;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="localAddress">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="transport">TBD</param>
        /// <param name="inbound">TBD</param>
        public TestAssociationHandle(Address localAddress, Address remoteAddress, TestTransport transport, bool inbound)
            : base(localAddress, remoteAddress)
        {
            Inbound = inbound;
            _transport = transport;
        }

        /// <summary>
        ///     Key used in <see cref="AssociationRegistry" /> to identify associations. Contains an ordered Tuple of addresses,
        ///     where the first address is always the initiator of the association.
        /// </summary>
        public (Address, Address) Key
        {
            get
            {
                return !Inbound
                    ? (LocalAddress, RemoteAddress)
                    : (RemoteAddress, LocalAddress);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="payload">TBD</param>
        /// <returns>TBD</returns>
        public override bool Write(ByteString payload)
        {
            if (Writeable)
            {
                var result = _transport.Write(this, payload);
                result.Wait(TimeSpan.FromSeconds(3));
                return result.Result;
            }

            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Disassociate()
        {
            _transport.Disassociate(this);
        }
    }
}
