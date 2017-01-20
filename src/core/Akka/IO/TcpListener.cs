﻿//-----------------------------------------------------------------------
// <copyright file="TcpListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    partial class TcpListener
    {
        /// <summary>
        /// TBD
        /// </summary>
        public class RegisterIncoming : SelectionHandler.IHasFailureMessage, INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="channel">TBD</param>
            public RegisterIncoming(SocketChannel channel)
            {
                Channel = channel;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public SocketChannel Channel { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public object FailureMessage
            {
                get { return new FailedRegisterIncoming(Channel); }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class FailedRegisterIncoming
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="channel">TBD</param>
            public FailedRegisterIncoming(SocketChannel channel)
            {
                Channel = channel;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public SocketChannel Channel { get; private set; }
        }
    }

    partial class TcpListener : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly IActorRef _selectorRouter;
        private readonly TcpExt _tcp;
        private readonly IActorRef _bindCommander;
        private readonly Tcp.Bind _bind;
        private readonly SocketChannel _channel;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private int acceptLimit;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="selectorRouter">TBD</param>
        /// <param name="tcp">TBD</param>
        /// <param name="channelRegistry">TBD</param>
        /// <param name="bindCommander">TBD</param>
        /// <param name="bind">TBD</param>
        public TcpListener(IActorRef selectorRouter, TcpExt tcp, IChannelRegistry channelRegistry, IActorRef bindCommander,
            Tcp.Bind bind)
        {
            _selectorRouter = selectorRouter;
            _tcp = tcp;
            _bindCommander = bindCommander;
            _bind = bind;

            Context.Watch(bind.Handler);

            _channel = SocketChannel.Open().ConfigureBlocking(false);

            acceptLimit = bind.PullMode ? 0 : _tcp.Settings.BatchAcceptLimit;
            
            var localAddress = new Func<EndPoint>(() =>
            {
                try
                {
                    var socket = _channel.Socket;
                    bind.Options.ForEach(x => x.BeforeServerSocketBind(socket));

                    socket.Bind(bind.LocalAddress);
                    socket.Listen(bind.Backlog);

                    channelRegistry.Register(_channel,
                        bind.PullMode ? SocketAsyncOperation.None : SocketAsyncOperation.Accept, Self);
                    return socket.LocalEndPoint;
                }
                catch (Exception e)
                {
                    _bindCommander.Tell(bind.FailureMessage);
                    _log.Error(e, "Bind failed for TCP channel on endpoint [{0}]", bind.LocalAddress);
                    Context.Stop(Self);
                }

                return bind.LocalAddress;
            })();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return SelectionHandler.ConnectionSupervisorStrategy;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            var registration = message as ChannelRegistration;
            if (registration != null)
            {
                _bindCommander.Tell(new Tcp.Bound(_channel.Socket.LocalEndPoint));
                Context.Become(Bound(registration));
                return true;
            }
            return false;
        }

        private Receive Bound(ChannelRegistration registration)
        {
            return message =>
            {
                if (message is SelectionHandler.ChannelAcceptable)
                {
                    acceptLimit = AcceptAllPending(registration, acceptLimit); 
                    if (acceptLimit > 0)
                        registration.EnableInterest(SocketAsyncOperation.Accept);
                    return true;
                }
                var resumeAccepting = message as Tcp.ResumeAccepting;
                if (resumeAccepting != null)
                {
                    acceptLimit = resumeAccepting.BatchSize;
                    registration.EnableInterest(SocketAsyncOperation.Accept);
                    return true;
                }
                var failedRegisterIncoming = message as FailedRegisterIncoming;
                if (failedRegisterIncoming != null)
                {
                    _log.Warning("Could not register incoming connection since selector capacity limit is reached, closing connection");
                    try
                    {
                        failedRegisterIncoming.Channel.Close();
                    }
                    catch (Exception ex)
                    {
                        //TODO: log.debug("Error closing socket channel: {}", e)
                    }
                    return true;
                }
                if (message is Tcp.Unbind)
                {
                    //TODO: log.debug("Unbinding endpoint {}", localAddress)
                    _channel.Close();
                    Sender.Tell(Tcp.Unbound.Instance);
                    //TODO: log.debug("Unbound endpoint {}, stopping listener", localAddress)
                    Context.Stop(Self);
                    return true;
                }
                return false;
            };
        }

        private int AcceptAllPending(ChannelRegistration registration, int limit)
        {
            var socketChannel = new Func<SocketChannel>(() =>
            {
                if (limit <= 0) return null;
                try
                {
                    return _channel.Accept();
                }
                catch (Exception ex)
                {
                    // log.error(e, "Accept error: could not accept new connection")
                    return null;
                }
            })();
            if (socketChannel != null)
            {
                Func<IChannelRegistry, Props> props = registry => Props.Create(
                    () => new TcpIncomingConnection(_tcp, socketChannel, registry, _bind.Handler, _bind.Options, _bind.PullMode));
                _selectorRouter.Tell(new SelectionHandler.WorkerForCommand(new RegisterIncoming(socketChannel), Self, props));
                return AcceptAllPending(registration, limit - 1);
            }
            if (_bind.PullMode) return limit;
            return _tcp.Settings.BatchAcceptLimit; 
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            if (_channel.IsOpen())
            {
                _channel.Close();
            }
        }
    }
}