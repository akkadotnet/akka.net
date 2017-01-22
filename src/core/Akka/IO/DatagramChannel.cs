//-----------------------------------------------------------------------
// <copyright file="DatagramChannel.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    public class DatagramChannel : SocketChannel
    {
        private DatagramChannel(Socket socket) : base(socket)
        {
            
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static DatagramChannel Open()
        {
            return new DatagramChannel(new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override bool IsOpen()
        {
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <param name="target">TBD</param>
        /// <returns>TBD</returns>
        public int Send(ByteBuffer buffer, EndPoint target)
        {
            if (!Socket.Poll(0, SelectMode.SelectWrite))
                return 0;
            var data = new byte[buffer.Remaining];
            buffer.Get(data);
            return Socket.SendTo(data, target);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <returns>TBD</returns>
        public EndPoint Receive(ByteBuffer buffer)
        {
            if (!Socket.Poll(0, SelectMode.SelectRead))
                return null;
            var ep = Socket.LocalEndPoint;
            var data = new byte[buffer.Remaining];
            var length = Socket.ReceiveFrom(data, ref ep);
            buffer.Put(data, 0, length);
            return ep;
        }
    }
}