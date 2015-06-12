//-----------------------------------------------------------------------
// <copyright file="Inet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Net.Sockets;

namespace Akka.IO
{
    public class Inet
    {
        public abstract class SocketOption
        {
            public virtual void BeforeDatagramBind(Socket ds) 
            { }

            public virtual void BeforeServerSocketBind(Socket ss)
            { }

            public virtual void BeforeConnect(Socket s)
            { }
            public virtual void AfterConnect(Socket s)
            { }
        }

        public abstract class AbstractSocketOption : SocketOption { }

        public abstract class SocketOptionV2 : SocketOption
        {
            public virtual void AfterBind(Socket s)
            { }
        }

        public abstract class AbstractSocketOptionV2 : SocketOptionV2 { }

        public class DatagramChannelCreator : SocketOption
        {
            public virtual DatagramChannel Create()
            {
                return DatagramChannel.Open();
            }
        }

        public static class SO
        {
            public class ReceiveBufferSize : SocketOption
            {
                private readonly int _size;

                public ReceiveBufferSize(int size)
                {
                    _size = size;
                }

                public override void BeforeServerSocketBind(Socket ss)
                {
                    ss.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, _size);
                }
                public override void BeforeDatagramBind(Socket ds)
                {
                    ds.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, _size);
                }
                public override void BeforeConnect(Socket s)
                {
                    s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, _size);
                }
            }

            public class ReuseAddress : SocketOption
            {
                private readonly bool _on;

                public ReuseAddress(bool on)
                {
                    _on = @on;
                }

                public override void BeforeServerSocketBind(Socket ss)
                {
                    ss.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _on);
                }
                public override void BeforeDatagramBind(Socket ds)
                {
                    ds.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _on);
                }
                public override void BeforeConnect(Socket s)
                {
                    s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _on);
                }
            }

            public class SendBufferSize : SocketOption
            {
                private readonly int _size;

                public SendBufferSize(int size)
                {
                    _size = size;
                }

                public override void AfterConnect(Socket s)
                {
                    s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, _size);
                }
            }

            public class TrafficClass : SocketOption
            {
                private readonly int _tc;

                public TrafficClass(int tc)
                {
                    _tc = tc;
                }

                public override void AfterConnect(Socket s)
                {
                    //TODO: What is the .NET equivalent
                }
            }
        }

        public abstract class SoForwarders
        {
            
        }
    }
}
