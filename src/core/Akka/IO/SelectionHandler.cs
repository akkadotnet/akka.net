//-----------------------------------------------------------------------
// <copyright file="SelectionHandler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#if AKKAIO
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Routing;
using Akka.Util.Internal;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract class SelectionHandlerSettings
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        protected SelectionHandlerSettings(Config config)
        {
            //TODO: requiring

            MaxChannels = config.GetString("max-channels") == "unlimited" 
                ? -1 
                : config.GetInt("max-channels");
            
            SelectorAssociationRetries = config.GetInt("selector-association-retries");

            SelectorDispatcher = config.GetString("selector-dispatcher");
            WorkerDispatcher = config.GetString("worker-dispatcher");
            TraceLogging = config.GetBoolean("trace-logging");
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int MaxChannels { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public int SelectorAssociationRetries { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public string SelectorDispatcher { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public string WorkerDispatcher { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public bool TraceLogging { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public int MaxChannelsPerSelector { get; protected set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal interface IChannelRegistry
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="channel">TBD</param>
        /// <param name="initialOps">TBD</param>
        /// <param name="channelActor">TBD</param>
        void Register(SocketChannel channel, SocketAsyncOperation? initialOps, IActorRef channelActor);
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class ChannelRegistration
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="enableInterest">TBD</param>
        /// <param name="disableInterest">TBD</param>
        public ChannelRegistration(Action<SocketAsyncOperation> enableInterest, Action<SocketAsyncOperation> disableInterest)
        {
            EnableInterest = enableInterest;
            DisableInterest = disableInterest;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Action<SocketAsyncOperation> EnableInterest { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Action<SocketAsyncOperation> DisableInterest { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class SelectionHandler : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        // OBJECT 

        /// <summary>
        /// TBD
        /// </summary>
        public interface IHasFailureMessage
        {
            object FailureMessage { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class WorkerForCommand : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="apiCommand">TBD</param>
            /// <param name="commander">TBD</param>
            /// <param name="childProps">TBD</param>
            public WorkerForCommand(IHasFailureMessage apiCommand, IActorRef commander, Func<IChannelRegistry, Props> childProps)
            {
                ApiCommand = apiCommand;
                Commander = commander;
                ChildProps = childProps;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IHasFailureMessage ApiCommand { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public IActorRef Commander { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public Func<IChannelRegistry, Props> ChildProps { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class Retry : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="command">TBD</param>
            /// <param name="retriesLeft">TBD</param>
            public Retry(WorkerForCommand command, int retriesLeft)
            {
                Command = command;
                RetriesLeft = retriesLeft;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public WorkerForCommand Command { get; private set; }
            /// <summary>
            /// TBD
            /// </summary>
            public int RetriesLeft { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class ChannelConnectable
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly ChannelConnectable Instance = new ChannelConnectable();

            private ChannelConnectable()
            { }
        }
        /// <summary>
        /// TBD
        /// </summary>
        public class ChannelAcceptable
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly ChannelAcceptable Instance = new ChannelAcceptable();

            private ChannelAcceptable()
            { }
        }
        /// <summary>
        /// TBD
        /// </summary>
        public class ChannelReadable : IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly ChannelReadable Instance = new ChannelReadable();

            private ChannelReadable()
            { }
        }
        /// <summary>
        /// TBD
        /// </summary>
        public class ChannelWritable : IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly ChannelWritable Instance = new ChannelWritable();

            private ChannelWritable()
            { }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract class SelectorBasedManager : ActorBase
        {
            /// <summary>
            /// TBD
            /// </summary>
            protected readonly IActorRef SelectorPool;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="selectorSettings">TBD</param>
            /// <param name="nrOfSelectors">TBD</param>
            protected SelectorBasedManager(SelectionHandlerSettings selectorSettings, int nrOfSelectors)
            {
                SelectorPool = Context.ActorOf(
                    props: new RandomPool(nrOfSelectors).Props(Props.Create(() => new SelectionHandler(selectorSettings)).WithDeploy(Deploy.Local)),
                    name: "selectors");
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            protected override SupervisorStrategy SupervisorStrategy()
            {
                return ConnectionSupervisorStrategy;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="pf">TBD</param>
            /// <returns>TBD</returns>
            protected Receive WorkerForCommandHandler(Func<IHasFailureMessage, Func<IChannelRegistry, Props>> pf)
            {
                return message =>
                {
                    var cmd = message as IHasFailureMessage;
                    if (cmd != null)
                    {
                        SelectorPool.Tell(new WorkerForCommand(cmd, Sender, pf(cmd)));
                        return true;
                    }
                    return false;
                };
            }
        }

        /* 
         * Special supervisor strategy for parents of TCP connection and listener actors.
         * Stops the child on all errors and logs DeathPactExceptions only at debug level.
         */
        private class ConnectionSupervisorStrategyImp : OneForOneStrategy
        {
            public ConnectionSupervisorStrategyImp()
                : base(StoppingStrategy.Decider)
            { }

            protected override void LogFailure(IActorContext context, IActorRef child, Exception cause, Directive directive)
            {
                if (cause is DeathPactException)
                {
                    try
                    {
                        Context.System.EventStream.Publish(new Debug(child.Path.ToString(), GetType(), "Closed after handler termination"));
                    }
                    catch (Exception _) { }
                }
                else base.LogFailure(context, child, cause, directive);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly SupervisorStrategy ConnectionSupervisorStrategy = new ConnectionSupervisorStrategyImp();

        private class ChannelRegistryImpl : IChannelRegistry
        {
            private readonly ILoggingAdapter _log;
            private readonly SingleThreadExecutionContext _executionContext;
            private readonly IDictionary<Socket, SocketChannel> _read = new Dictionary<Socket, SocketChannel>();
            private readonly IDictionary<Socket, SocketChannel> _write = new Dictionary<Socket, SocketChannel>();

            public ChannelRegistryImpl(ILoggingAdapter log)
            {
                _log = log;
                _executionContext = new SingleThreadExecutionContext();
            }

            private void Execute(Action action)
            {
                _executionContext.Execute(action);
            }

            private void Select()
            {
                if (_read.Count == 0 && _write.Count == 0) return;  // Stop select loop when no more interested sockets. It will be started again once a socket is registered

                var readable = _read.Keys.ToList();
                var writeable = _write.Keys.ToList();
                try
                {
                    Socket.Select(readable, writeable, null, 1);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.NotSocket)
                {
                }

                foreach (var socket in readable)
                {
                    var channel = _read[socket];
                    if (channel.IsOpen())
                        channel.Connection.Tell(ChannelReadable.Instance);
                    else
                        channel.Connection.Tell(ChannelAcceptable.Instance);
                    _read.Remove(socket);
                }
                foreach (var socket in writeable)
                {
                    var channel = _write[socket];
                    if (channel.IsOpen())
                        channel.Connection.Tell(ChannelWritable.Instance);
                    else
                        channel.Connection.Tell(ChannelConnectable.Instance);
                    _write.Remove(socket);
                }
                Execute(Select);
            }

            public void Register(SocketChannel channel, SocketAsyncOperation? initialOps, IActorRef channelActor)
            {
                channel.Register(channelActor, initialOps);

                if (initialOps.HasValue)
                    EnableInterest(channel, initialOps.Value);

                channelActor.Tell(new ChannelRegistration(
                    enableInterest: op => EnableInterest(channel, op), 
                    disableInterest: op => DisableInterest(channel, op) 
                    ));
            }

            private void EnableInterest(SocketChannel channel, SocketAsyncOperation op)
            {
                switch (op)
                {
                    case SocketAsyncOperation.Accept:
                    case SocketAsyncOperation.Receive:
                        Execute(() =>
                        {
                            _read.Add(channel.Socket, channel);
                            if (_read.Count == 1 && _write.Count == 0)  // Start the select loop on initial enable interest
                                Select();                               // The select loop will stop itself if no more interested sockets
                        });
                        break;
                    case SocketAsyncOperation.Connect:
                    case SocketAsyncOperation.Send:
                        Execute(() =>
                        {
                            _write.Add(channel.Socket, channel);        // Start the select loop on initial enable interest
                            if (_read.Count == 0 && _write.Count == 1)  // The select loop will stop itself if no more interested sockets
                                Select();
                        });
                        break;
                }
            }
            private void DisableInterest(SocketChannel channel, SocketAsyncOperation op)
            {
                switch (op)
                {
                    case SocketAsyncOperation.Accept:
                    case SocketAsyncOperation.Receive:
                        Execute(() => _read.Remove(channel.Socket));
                        break;
                    case SocketAsyncOperation.Connect:
                    case SocketAsyncOperation.Send:
                        Execute(() => _write.Remove(channel.Socket));
                        break;
                }
            }

            public void Shutdown()
            {
                _executionContext.Stop();
            }
        }

        // CLASS
        private readonly SelectionHandlerSettings _settings;
        private readonly ChannelRegistryImpl _registry;
        private int _sequenceNumber;
        private int _childCount;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        public SelectionHandler(SelectionHandlerSettings settings)
        {
            _settings = settings;
            _registry = new ChannelRegistryImpl(Context.GetLogger());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            var cmd = message as WorkerForCommand;
            if (cmd != null)
            {
                SpawnChildWithCapacityProtection(cmd, _settings.SelectorAssociationRetries);
                return true;
            }
            var retry = message as Retry;
            if (retry != null)
            {
                SpawnChildWithCapacityProtection(retry.Command, retry.RetriesLeft);
                return true;
            }
            var _ = message as Terminated;
            if (_ != null)
            {
                _childCount -= 1;
                return true;
            }
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            _registry.Shutdown();
        }

        // we can never recover from failures of a connection or listener child
        // and log the failure at debug level
        private class SelectionHandlerSupervisorStrategy : OneForOneStrategy
        {
            public SelectionHandlerSupervisorStrategy()
                : base(StoppingStrategy.Decider)
            { }

            protected override void LogFailure(IActorContext context, IActorRef child, Exception cause, Directive directive)
            {
                try
                {
                    var e = cause as ActorInitializationException;
                    var logMessage = e != null
                        ? e.GetBaseException() .Message
                        : cause.Message;
                    Context.System.EventStream.Publish(
                        new Debug(child.Path.ToString(), typeof (SelectionHandler), logMessage));
                }
                catch(Exception _) { }
            }
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new SelectionHandlerSupervisorStrategy();
        }

        private void SpawnChildWithCapacityProtection(WorkerForCommand cmd, int retriesLeft)
        {
            var log = Context.GetLogger();
            if (_settings.TraceLogging) log.Debug("Executing [{0}]", cmd);
            if (_settings.MaxChannelsPerSelector == -1 || _childCount < _settings.MaxChannelsPerSelector)
            {
                var newName = _sequenceNumber.ToString(CultureInfo.InvariantCulture);
                _sequenceNumber += 1;
                var child = Context.ActorOf(props: cmd.ChildProps(_registry) 
                                                      .WithDispatcher(_settings.WorkerDispatcher)
                                                      .WithDeploy(Deploy.Local), 
                                            name: newName);
                _childCount += 1;
                if (_settings.MaxChannelsPerSelector > 0) Context.Watch(child);
            }
            else
            {
                if (retriesLeft >= 1)
                {
                    log.Debug("Rejecting [{0}] with [{1}] retries left, retrying...", cmd, retriesLeft);
                    Context.Parent.Forward(new Retry(cmd, retriesLeft - 1));
                }
                else
                {
                    log.Warning("Rejecting [{0}] with no retries left, aborting...", cmd);
                    cmd.Commander.Tell(cmd.ApiCommand.FailureMessage);
                }
            }
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    class SingleThreadExecutionContext
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();

        /// <summary>
        /// TBD
        /// </summary>
        public SingleThreadExecutionContext()
        {
            Task.Factory.StartNew(() =>
            {
                foreach (var action in _queue.GetConsumingEnumerable())
                    action();
            }, TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="action">TBD</param>
        public void Execute(Action action)
        {
            try
            {
                if (!_queue.IsAddingCompleted)
                    _queue.Add(action);
            }
            catch 
            {
                //ignore adding completed
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Stop()
        {
            _queue.CompleteAdding();
        }
    }
}
#endif