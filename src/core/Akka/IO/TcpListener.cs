//-----------------------------------------------------------------------
// <copyright file="TcpListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// <summary>
    /// INTERNAL API
    ///
    /// TcpListener is an internal actor that binds to a local address and listens for incoming TCP connections.
    /// </summary>
    internal sealed class TcpListener : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        /// <summary>
        /// In the event that someone specified something stupid like 0 or a negative number
        /// for the accept limit, this is a reasonable default.
        /// </summary>
        public static readonly int DefaultAcceptLimit = Environment.ProcessorCount * 2;
        
        private readonly TcpExt _tcp;
        private readonly IActorRef _bindCommander;  // forwarded destination for Connected
        private Tcp.Bind _bind;
        private Socket _socket;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private int _acceptLimit;
        private SocketAsyncEventArgs[]? _acceptPool;
        private bool _binding;

        private sealed record AcceptCompleted(SocketAsyncEventArgs EventArgs) : INoSerializationVerificationNeeded;

        private sealed record RetryAccept(SocketAsyncEventArgs EventArgs) : INoSerializationVerificationNeeded;
        
        public TcpListener(TcpExt tcp, IActorRef bindCommander,
            Tcp.Bind bind)
        {
            _tcp = tcp;
            _bindCommander = bindCommander;
            
            Self.Tell(bind);
        }

        private Receive Bound() => message =>
        {
            switch (message)
            {
                case AcceptCompleted accepted:
                    HandleAccept(accepted.EventArgs);
                    return true;
                
                case RetryAccept retry:
                    StartAccept(retry.EventArgs);
                    return true;

                case Tcp.ResumeAccepting:
                    // NO-OP - this is obsolete
                    return true;

                case Tcp.Unbind:
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
        
        private void StartAccept(SocketAsyncEventArgs saea)
        {

            var pending = _socket.AcceptAsync(saea);
            if (!pending)
                Self.Tell(new AcceptCompleted(saea), Self); // synchronous completion ➔ mailbox
        }

        // closure
        private IActorRef _safeSelf = Context.Self;

        private void OnCompleted(object? sender, SocketAsyncEventArgs saea)
        {
            // Marshall back into the actor context – keeps user code off the IOCP thread.
            _safeSelf.Tell(new AcceptCompleted(saea), ActorRefs.NoSender);
        }
        
        private void HandleAccept(SocketAsyncEventArgs saea)
        {
            switch (saea.SocketError)
            {
                case SocketError.Success:
                    var accepted = saea.AcceptSocket!;
                    saea.AcceptSocket = null;          // ready for re‑use
                    Context.ActorOf(Props.Create<TcpIncomingConnection>(_tcp, accepted, _bind.Handler, _bind.Options, _bind.PullMode).WithDeploy(Deploy.Local));
                    StartAccept(saea);                 // keep the pool full
                    break;

                case SocketError.ConnectionReset:
                case SocketError.NoBufferSpaceAvailable:
                case SocketError.TryAgain:
                case SocketError.TimedOut:
                case SocketError.WouldBlock:
                    // transient – short back‑off then retry
                    saea.AcceptSocket = null;
                    Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(10), Self,
                        new RetryAccept(saea), ActorRefs.NoSender);
                    break;
                default:
                    _log.Error("Fatal socket error in TcpListener: {0}", saea.SocketError);
                    Context.Stop(Self);
                    break;
            }
        }

        private Task<Tcp.Bound> BindAsync()
        {
            try
            {
                _socket = new Socket(_bind.LocalAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    Blocking = false
                };

                _bind.Options.ForEach(x => x.BeforeServerSocketBind(_socket));
                _socket.Bind(_bind.LocalAddress);
                _socket.Listen(_bind.Backlog);
                
                _acceptPool = new SocketAsyncEventArgs[_acceptLimit];
                for (var i = 0; i < _acceptPool.Length; i++)
                {
                    var saea = new SocketAsyncEventArgs();
                    saea.Completed += OnCompleted;  // IOCP -> this actor
                    _acceptPool[i] = saea;
                }
                
                // start accepting connections
                foreach (var saea in _acceptPool)
                    StartAccept(saea);

                return Task.FromResult(new Tcp.Bound(_socket.LocalEndPoint));
            }
            catch (Exception ex)
            {
                return Task.FromException<Tcp.Bound>(ex);
            }
        }

        private Task<Tcp.Unbound> UnbindAsync()
        {
            try
            {
                _log.Debug("Unbinding endpoint {0}", _bind.LocalAddress);
                _socket.Close();
                return Task.FromResult(Tcp.Unbound.Instance);
            }
            catch (Exception ex)
            {
                return Task.FromException<Tcp.Unbound>(ex);
            }
        }
        
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return Tcp.ConnectionSupervisorStrategy;
        }

        protected override bool Receive(object message)
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
                    // not going to do pull mode
                    _acceptLimit = _tcp.Settings.BatchAcceptLimit;
                    if (_acceptLimit <= 0)
                    {
                        _log.Debug("Accept limit was set to {0}, which is unworkable. Using default value of {1} (logical CPUs * 2)", _acceptLimit, DefaultAcceptLimit);
                        _acceptLimit = DefaultAcceptLimit;
                    }
                        
                    
                    _log.Info("Binding TCP channel on endpoint [{0}]", _bind.LocalAddress); 
                    
                    BindAsync().PipeTo(Self);
                    return true;
                
                case Status.Failure fail:
                    _bindCommander.Tell(_bind.FailureMessage.WithCause(fail.Cause));
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
        }
        
        protected override void PostStop()
        {
            try
            {
                if(_acceptPool != null)
                    foreach(var saea in _acceptPool)
                    {
                        // remove event handler
                        saea.Completed -= OnCompleted;
                        saea.Dispose();
                    }
                _socket?.Dispose();
            }
            catch (Exception e)
            {
                _log.Debug("Error closing ServerSocketChannel: {0}", e);
            }
        }
    }
}
