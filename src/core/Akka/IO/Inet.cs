//-----------------------------------------------------------------------
// <copyright file="Inet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Net.Sockets;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    public class Inet
    {
        /// <summary>
        /// TBD
        /// </summary>
        public abstract class SocketOption
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="ds">TBD</param>
            public virtual void BeforeDatagramBind(Socket ds) 
            { }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="ss">TBD</param>
            public virtual void BeforeServerSocketBind(Socket ss)
            { }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="s">TBD</param>
            public virtual void BeforeConnect(Socket s)
            { }
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="s">TBD</param>
            public virtual void AfterConnect(Socket s)
            { }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract class AbstractSocketOption : SocketOption { }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract class SocketOptionV2 : SocketOption
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="s">TBD</param>
            public virtual void AfterBind(Socket s)
            { }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract class AbstractSocketOptionV2 : SocketOptionV2 { }

        /// <summary>
        /// TBD
        /// </summary>
        public class DatagramChannelCreator : SocketOption
        {
            public virtual Socket Create()
            {
                return new Socket(SocketType.Dgram, ProtocolType.Udp);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static class SO
        {
            /// <summary>
            /// TBD
            /// </summary>
            public class ReceiveBufferSize : SocketOption
            {
                private readonly int _size;

                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="size">TBD</param>
                public ReceiveBufferSize(int size)
                {
                    _size = size;
                }

                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="ss">TBD</param>
                public override void BeforeServerSocketBind(Socket ss)
                {
                    ss.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, _size);
                }
                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="ds">TBD</param>
                public override void BeforeDatagramBind(Socket ds)
                {
                    ds.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, _size);
                }
                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="s">TBD</param>
                public override void BeforeConnect(Socket s)
                {
                    s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, _size);
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            public class ReuseAddress : SocketOption
            {
                private readonly bool _on;

                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="on">TBD</param>
                public ReuseAddress(bool on)
                {
                    _on = @on;
                }

                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="ss">TBD</param>
                public override void BeforeServerSocketBind(Socket ss)
                {
                    ss.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _on);
                }
                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="ds">TBD</param>
                public override void BeforeDatagramBind(Socket ds)
                {
                    ds.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _on);
                }
                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="s">TBD</param>
                public override void BeforeConnect(Socket s)
                {
                    s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _on);
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            public class SendBufferSize : SocketOption
            {
                private readonly int _size;

                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="size">TBD</param>
                public SendBufferSize(int size)
                {
                    _size = size;
                }

                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="s">TBD</param>
                public override void AfterConnect(Socket s)
                {
                    s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, _size);
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            public class TrafficClass : SocketOption
            {
                private readonly int _tc;

                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="tc">TBD</param>
                public TrafficClass(int tc)
                {
                    _tc = tc;
                }

                /// <summary>
                /// TBD
                /// </summary>
                /// <param name="s">TBD</param>
                public override void AfterConnect(Socket s)
                {
                    //TODO: What is the .NET equivalent
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract class SoForwarders
        {
            
        }
    }
}
