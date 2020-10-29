//-----------------------------------------------------------------------
// <copyright file="TcpListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util.Internal;
using System.Collections.Generic;
using System.Linq;

namespace Akka.IO
{
    partial class TcpListener : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly TcpExt _tcp;
        private readonly IActorRef _bindCommander;
        private readonly Tcp.Bind _bind;
        private readonly Socket _socket;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private int _acceptLimit;
        private SocketAsyncEventArgs[] _saeas;
        
        private int acceptLimit;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="tcp">TBD</param>
        /// <param name="bindCommander">TBD</param>
        /// <param name="bind">TBD</param>
        public TcpListener(TcpExt tcp, IActorRef bindCommander,
            Tcp.Bind bind)
        {
            _tcp = tcp;
            _bindCommander = bindCommander;
            _bind = bind;

            Context.Watch(bind.Handler);

            _socket = new Socket(_bind.LocalAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };

            _acceptLimit = bind.PullMode ? 0 : _tcp.Settings.BatchAcceptLimit;
            
            try
            {
                bind.Options.ForEach(x => x.BeforeServerSocketBind(_socket));
                _socket.Bind(bind.LocalAddress);
                _socket.Listen(bind.Backlog);
                _saeas = Accept(_acceptLimit).ToArray();
            }
            catch (Exception e)
            {
                _bindCommander.Tell(bind.FailureMessage);
                _log.Error(e, "Bind failed for TCP channel on endpoint [{0}]", bind.LocalAddress);
                Context.Stop(Self);
            }

            bindCommander.Tell(new Tcp.Bound(_socket.LocalEndPoint));
        }
        
        private IEnumerable<SocketAsyncEventArgs> Accept(int limit)
        {
            for(var i = 0; i < _acceptLimit; i++)
            {
                var self = Self;
                var saea = new SocketAsyncEventArgs();
                saea.Completed += (s, e) => self.Tell(e);
                if (!_socket.AcceptAsync(saea))
                    Self.Tell(saea);
                yield return saea;
            }
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return Tcp.ConnectionSupervisorStrategy;
        }

        protected override bool Receive(object message)
        {
            if (message is SocketAsyncEventArgs)
            {
                var saea = message as SocketAsyncEventArgs;
                if (saea.SocketError == SocketError.Success)
                    Context.ActorOf(Props.Create<TcpIncomingConnection>(_tcp, saea.AcceptSocket, _bind.Handler, _bind.Options, _bind.PullMode));
                saea.AcceptSocket = null;

                if (!_socket.AcceptAsync(saea))
                    Self.Tell(saea);
                return true;
            }
            var resumeAccepting = message as Tcp.ResumeAccepting;
            if (resumeAccepting != null)
            {
                _acceptLimit = resumeAccepting.BatchSize;
                _saeas = Accept(_acceptLimit).ToArray();
                return true;
            }
            if (message is Tcp.Unbind)
            {
                _log.Debug("Unbinding endpoint {0}", _bind.LocalAddress);
                 _socket.Dispose();
                Sender.Tell(Tcp.Unbound.Instance);
                _log.Debug("Unbound endpoint {0}, stopping listener", _bind.LocalAddress);
                Context.Stop(Self);
                return true;
            }
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            try
            {
                _socket.Dispose();
                _saeas?.ForEach(x => x.Dispose());
            }
            catch (Exception e)
            {
                _log.Debug("Error closing ServerSocketChannel: {0}", e);
            }
        }
    }
}
