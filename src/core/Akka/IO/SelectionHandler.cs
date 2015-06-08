//-----------------------------------------------------------------------
// <copyright file="IO.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
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

namespace Akka.IO
{
    public abstract class SelectionHandlerSettings
    {
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

        public int MaxChannels { get; private set; }
        public int SelectorAssociationRetries { get; private set; }
        public string SelectorDispatcher { get; private set; }
        public string WorkerDispatcher { get; private set; }
        public bool TraceLogging { get; private set; }
        
        public int MaxChannelsPerSelector { get; protected set; }
    }

    internal interface IChannelRegistry
    {
        void Register(SocketChannel channel, SocketAsyncOperation? initialOps, IActorRef channelActor);
    }

    internal class ChannelRegistration
    {
        public ChannelRegistration(Action<SocketAsyncOperation> enableInterest, Action<SocketAsyncOperation> disableInterest)
        {
            EnableInterest = enableInterest;
            DisableInterest = disableInterest;
        }

        public Action<SocketAsyncOperation> EnableInterest { get; private set; }
        public Action<SocketAsyncOperation> DisableInterest { get; private set; }
    }

    internal class SelectionHandler : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        // OBJECT 

        public interface IHasFailureMessage
        {
            object FailureMessage { get; }
        }

        public class WorkerForCommand : INoSerializationVerificationNeeded
        {
            public WorkerForCommand(IHasFailureMessage apiCommand, IActorRef commander, Func<IChannelRegistry, Props> childProps)
            {
                ApiCommand = apiCommand;
                Commander = commander;
                ChildProps = childProps;
            }

            public IHasFailureMessage ApiCommand { get; private set; }
            public IActorRef Commander { get; private set; }
            public Func<IChannelRegistry, Props> ChildProps { get; private set; }
        }

        public class Retry : INoSerializationVerificationNeeded
        {
            public Retry(WorkerForCommand command, int retriesLeft)
            {
                Command = command;
                RetriesLeft = retriesLeft;
            }

            public WorkerForCommand Command { get; private set; }
            public int RetriesLeft { get; private set; }
        }

        public class ChannelConnectable
        {
            public static readonly ChannelConnectable Instance = new ChannelConnectable();

            private ChannelConnectable()
            { }
        }
        public class ChannelAcceptable
        {
            public static readonly ChannelAcceptable Instance = new ChannelAcceptable();

            private ChannelAcceptable()
            { }
        }
        public class ChannelReadable
        {
            public static readonly ChannelReadable Instance = new ChannelReadable();

            private ChannelReadable()
            { }
        }
        public class ChannelWritable
        {
            public static readonly ChannelWritable Instance = new ChannelWritable();

            private ChannelWritable()
            { }
        }

        public abstract class SelectorBasedManager : ActorBase
        {
            protected readonly IActorRef SelectorPool;

            protected SelectorBasedManager(SelectionHandlerSettings selectorSettings, int nrOfSelectors)
            {
                SelectorPool = Context.ActorOf(
                    props: new RandomPool(nrOfSelectors).Props(Props.Create(() => new SelectionHandler(selectorSettings)).WithDeploy(Deploy.Local)),
                    name: "selectors");
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return ConnectionSupervisorStrategy;
            }

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
        public static readonly SupervisorStrategy ConnectionSupervisorStrategy = new ConnectionSupervisorStrategyImp();

        private class ChannelRegistryImpl : IChannelRegistry
        {
            private readonly ILoggingAdapter _log;
            private readonly SocketAsyncEventArgsPool _pool;

            public ChannelRegistryImpl(ILoggingAdapter log)
            {
                _log = log;
                //_pool = new SocketAsyncEventArgsPool(1000, SocketChannel.Select);

                Task.Run(() => Select());
            }

            private readonly IDictionary<Socket, SocketChannel> read = new Dictionary<Socket, SocketChannel>();
            private readonly IDictionary<Socket, SocketChannel> write = new Dictionary<Socket, SocketChannel>();
            private readonly IList error = new List<Socket>();

            private void Select()
            {
                if (read.Count > 0 || write.Count > 0)
                {
                    var readable = read.Keys.ToList();
                    var writeable = write.Keys.ToList();
                    Socket.Select(readable, writeable, error, 100);
                    foreach (var socket in readable)
                    {
                        var channel = read[socket];
                        if (channel.IsOpen())
                            channel.Connection.Tell(ChannelReadable.Instance);
                        else
                            channel.Connection.Tell(ChannelAcceptable.Instance);
                        read.Remove(socket);
                    }
                    foreach (var socket in writeable)
                    {
                        var channel = write[socket];
                        if (channel.IsOpen())
                            channel.Connection.Tell(ChannelWritable.Instance);
                        else
                            channel.Connection.Tell(ChannelConnectable.Instance);
                        write.Remove(socket);
                    }
                }
                Task.Run(() => Select());
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
                            read.Add(channel.Socket, channel);
                            break;
                        case SocketAsyncOperation.Connect:
                        case SocketAsyncOperation.Send:
                            write.Add(channel.Socket, channel);
                            break;
                }
            }
            private void DisableInterest(SocketChannel channel, SocketAsyncOperation op)
            {
                switch (op)
                {
                    case SocketAsyncOperation.Accept:
                    case SocketAsyncOperation.Receive:
                        read.Remove(channel.Socket);
                        break;
                    case SocketAsyncOperation.Connect:
                    case SocketAsyncOperation.Send:
                        write.Remove(channel.Socket);
                        break;
                }
            }

            public void Shutdown()
            {
                //TODO: ??
            }
        }

        // CLASS
        private readonly SelectionHandlerSettings _settings;
        private readonly ChannelRegistryImpl _registry;
        private int _sequenceNumber;
        private int _childCount;

        public SelectionHandler(SelectionHandlerSettings settings)
        {
            _settings = settings;
            _registry = new ChannelRegistryImpl(Context.GetLogger());
        }

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
                    var e = (ActorInitializationException) cause;
                    var logMessage = e != null
                        ? e.GetBaseException() .Message
                        : cause.Message;
                    Context.System.EventStream.Publish(
                        new Debug(child.Path.ToString(), typeof (SelectionHandler), logMessage));
                }
                catch(Exception _) { }
            }
        }
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
}
