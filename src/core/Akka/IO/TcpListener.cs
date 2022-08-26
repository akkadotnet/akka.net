//-----------------------------------------------------------------------
// <copyright file="TcpListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using System.Threading.Tasks;

namespace Akka.IO
{
    class TcpListener : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly TcpExt _tcp;
        private readonly IActorRef _bindCommander;
        private Tcp.Bind _bind;
        private Socket _socket;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private int _acceptLimit;
        private SocketAsyncEventArgs[] _saeas;
        private bool _binding;

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
            
            Become(Initializing());
            Self.Tell(bind);
        }

        private Receive Initializing() => message =>
        {
            switch (message)
            {
                case Tcp.Bind bind:
                    if (_binding)
                    {
                        _log.Warning("Already trying to bind to TCP channel on endpoint [{0}]", _bind.LocalAddress);
                        return true;
                    }
                    _binding = true;
                    _bind = bind;
                    _acceptLimit = bind.PullMode ? 0 : _tcp.Settings.BatchAcceptLimit;
                    BindAsync().PipeTo(Self);
                    return true;
                
                case Status.Failure fail:
                    _bindCommander.Tell(_bind.FailureMessage);
                    _log.Error(fail.Cause, "Bind failed for TCP channel on endpoint [{0}]", _bind.LocalAddress);
                    Context.Stop(Self);
                    _binding = false;
                    return true;
                
                case Tcp.Bound bound:
                    Context.Watch(_bind.Handler);
                    _bindCommander.Tell(bound);
                    Become(Bound());
                    _binding = false;
                    return true;
                
                default:
                    return false;
            }
        };

        private Receive Bound() => message =>
        {
            switch (message)
            {
                case SocketEvent evt:
                    var saea = evt.Args;
                    if (saea.SocketError == SocketError.Success)
                        Context.ActorOf(Props.Create<TcpIncomingConnection>(_tcp, saea.AcceptSocket, _bind.Handler, _bind.Options, _bind.PullMode).WithDeploy(Deploy.Local));
                    
                    saea.AcceptSocket = null;
                    if (!_socket.AcceptAsync(saea))
                        Self.Tell(new SocketEvent(saea));
                    return true;

                case Tcp.ResumeAccepting resumeAccepting:
                    _acceptLimit = resumeAccepting.BatchSize;
                    // TODO: this is dangerous, previous async args are not disposed and there's no guarantee that they're not still receiving data
                    _saeas = Accept(_acceptLimit).ToArray();
                    return true;

                case Tcp.Unbind _:
                    Become(Unbinding(Sender));
                    UnbindAsync().PipeTo(Self);
                    return true;

                default:
                    return false;
            }
        };

        private Receive Unbinding(IActorRef requester) => message =>
        {
            switch (message)
            {
                case Tcp.Unbound unbound:
                    requester.Tell(unbound);
                    _log.Debug("Unbound endpoint {0}, stopping listener", _bind.LocalAddress);
                    Context.Stop(Self);
                    return true;
                
                case Status.Failure fail:
                    _log.Error(fail.Cause, "Failed to unbind TCP listener for address [{0}]", _bind.LocalAddress);
                    Context.Stop(Self);
                    return true;
                
                default:
                    return false;
            }
        };
        
        private IEnumerable<SocketAsyncEventArgs> Accept(int limit)
        {
            for(var i = 0; i < limit; i++)
            {
                var self = Self;
                var saea = new SocketAsyncEventArgs();
                saea.Completed += (s, e) => self.Tell(new SocketEvent(e));
                if (!_socket.AcceptAsync(saea))
                    Self.Tell(new SocketEvent(saea));
                yield return saea;
            }
        }

        private async Task<Tcp.Bound> BindAsync()
        {
            _socket = new Socket(_bind.LocalAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
                
            _bind.Options.ForEach(x => x.BeforeServerSocketBind(_socket));
            _socket.Bind(_bind.LocalAddress);
            _socket.Listen(_bind.Backlog);
            _saeas = Accept(_acceptLimit).ToArray();
                
            return new Tcp.Bound(_socket.LocalEndPoint);
        }

        private async Task<Tcp.Unbound> UnbindAsync()
        {
            _log.Debug("Unbinding endpoint {0}", _bind.LocalAddress);
            _socket.Close();
            return Tcp.Unbound.Instance;
        }
        
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return Tcp.ConnectionSupervisorStrategy;
        }

        protected override bool Receive(object message)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            try
            {
                _socket?.Dispose();
                _saeas?.ForEach(x => x.Dispose());
            }
            catch (Exception e)
            {
                _log.Debug("Error closing ServerSocketChannel: {0}", e);
            }
        }

        private readonly struct SocketEvent : INoSerializationVerificationNeeded
        {
            public readonly SocketAsyncEventArgs Args;

            public SocketEvent(SocketAsyncEventArgs args)
            {
                Args = args;
            }
        }
    }
}
