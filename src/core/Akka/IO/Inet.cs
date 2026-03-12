//-----------------------------------------------------------------------
// <copyright file="Inet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net.Sockets;

namespace Akka.IO
{
    /// <summary>
    /// Contains socket option classes used to configure TCP, UDP, and other socket-based I/O channels.
    /// </summary>
    public class Inet
    {
        /// <summary>
        /// Base class for socket options that can be applied at various stages of a socket's lifecycle.
        /// </summary>
        public abstract class SocketOption
        {
            /// <summary>
            /// Called before binding a datagram (UDP) socket. Override to configure socket options prior to binding.
            /// </summary>
            /// <param name="ds">The datagram socket to configure.</param>
            public virtual void BeforeDatagramBind(Socket ds) 
            { }

            /// <summary>
            /// Called before binding a server (TCP listener) socket. Override to configure socket options prior to binding.
            /// </summary>
            /// <param name="ss">The server socket to configure.</param>
            public virtual void BeforeServerSocketBind(Socket ss)
            { }

            /// <summary>
            /// Called before a client socket connects. Override to configure socket options prior to connecting.
            /// </summary>
            /// <param name="s">The socket to configure.</param>
            public virtual void BeforeConnect(Socket s)
            { }
            /// <summary>
            /// Called after a client socket connects. Override to configure socket options after connecting.
            /// </summary>
            /// <param name="s">The connected socket to configure.</param>
            public virtual void AfterConnect(Socket s)
            { }
        }

        /// <summary>
        /// Abstract base class for user-defined socket options. Inherits from <see cref="SocketOption"/>.
        /// </summary>
        public abstract class AbstractSocketOption : SocketOption { }

        /// <summary>
        /// Extended socket option that adds an <see cref="AfterBind"/> hook called after a socket is bound.
        /// </summary>
        public abstract class SocketOptionV2 : SocketOption
        {
            /// <summary>
            /// Called after a socket is bound to an address. Override to configure socket options after binding.
            /// </summary>
            /// <param name="s">The bound socket to configure.</param>
            public virtual void AfterBind(Socket s)
            { }
        }

        /// <summary>
        /// Abstract base class for user-defined extended socket options. Inherits from <see cref="SocketOptionV2"/>.
        /// </summary>
        public abstract class AbstractSocketOptionV2 : SocketOptionV2 { }

        /// <summary>
        /// A <see cref="SocketOption"/> that creates datagram (UDP) sockets for the specified address family.
        /// </summary>
        public class DatagramChannelCreator : SocketOption
        {
            public virtual Socket Create(AddressFamily addressFamily)
            {
                return new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
            }
        }

        /// <summary>
        /// Contains standard socket option implementations (receive buffer size, send buffer size, reuse address, traffic class).
        /// </summary>
        public static class SO
        {
            /// <summary>
            /// Socket option that sets the <see cref="SocketOptionName.ReceiveBuffer"/> size on a socket.
            /// </summary>
            public class ReceiveBufferSize : SocketOption
            {
                private readonly int _size;

                /// <summary>
                /// Creates a new <see cref="ReceiveBufferSize"/> option with the specified buffer size.
                /// </summary>
                /// <param name="size">The receive buffer size in bytes.</param>
                public ReceiveBufferSize(int size)
                {
                    _size = size;
                }

                /// <summary>
                /// Sets the receive buffer size on the server socket before binding.
                /// </summary>
                /// <param name="ss">The server socket to configure.</param>
                public override void BeforeServerSocketBind(Socket ss)
                {
                    ss.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, _size);
                }
                /// <summary>
                /// Sets the receive buffer size on the datagram socket before binding.
                /// </summary>
                /// <param name="ds">The datagram socket to configure.</param>
                public override void BeforeDatagramBind(Socket ds)
                {
                    ds.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, _size);
                }
                /// <summary>
                /// Sets the receive buffer size on the socket before connecting.
                /// </summary>
                /// <param name="s">The socket to configure.</param>
                public override void BeforeConnect(Socket s)
                {
                    s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, _size);
                }
            }

            /// <summary>
            /// Socket option that enables or disables <see cref="SocketOptionName.ReuseAddress"/> on a socket.
            /// </summary>
            public class ReuseAddress : SocketOption
            {
                private readonly bool _on;

                /// <summary>
                /// Creates a new <see cref="ReuseAddress"/> option.
                /// </summary>
                /// <param name="on"><see langword="true"/> to enable address reuse; <see langword="false"/> to disable it.</param>
                public ReuseAddress(bool on)
                {
                    _on = @on;
                }

                /// <summary>
                /// Sets the reuse address option on the server socket before binding.
                /// </summary>
                /// <param name="ss">The server socket to configure.</param>
                public override void BeforeServerSocketBind(Socket ss)
                {
                    ss.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _on);
                }
                /// <summary>
                /// Sets the reuse address option on the datagram socket before binding.
                /// </summary>
                /// <param name="ds">The datagram socket to configure.</param>
                public override void BeforeDatagramBind(Socket ds)
                {
                    ds.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _on);
                }
                /// <summary>
                /// Sets the reuse address option on the socket before connecting.
                /// </summary>
                /// <param name="s">The socket to configure.</param>
                public override void BeforeConnect(Socket s)
                {
                    s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _on);
                }
            }

            /// <summary>
            /// Socket option that sets the <see cref="SocketOptionName.SendBuffer"/> size on a socket after connecting.
            /// </summary>
            public class SendBufferSize : SocketOption
            {
                private readonly int _size;

                /// <summary>
                /// Creates a new <see cref="SendBufferSize"/> option with the specified buffer size.
                /// </summary>
                /// <param name="size">The send buffer size in bytes.</param>
                public SendBufferSize(int size)
                {
                    _size = size;
                }

                /// <summary>
                /// Sets the send buffer size on the socket after connecting.
                /// </summary>
                /// <param name="s">The connected socket to configure.</param>
                public override void AfterConnect(Socket s)
                {
                    s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, _size);
                }
            }

            /// <summary>
            /// Socket option that sets the IP traffic class (type-of-service) on a socket after connecting.
            /// </summary>
            public class TrafficClass : SocketOption
            {
                private readonly int _tc;

                /// <summary>
                /// Creates a new <see cref="TrafficClass"/> option with the specified traffic class value.
                /// </summary>
                /// <param name="tc">The traffic class (type-of-service) value.</param>
                public TrafficClass(int tc)
                {
                    _tc = tc;
                }

                /// <summary>
                /// Intended to set the traffic class on the socket after connecting. Currently not implemented.
                /// </summary>
                /// <param name="s">The connected socket to configure.</param>
                public override void AfterConnect(Socket s)
                {
                    //TODO: What is the .NET equivalent
                }
            }
        }

        /// <summary>
        /// Abstract base class for forwarding standard socket option definitions.
        /// </summary>
        public abstract class SoForwarders
        {
            
        }
    }
}
