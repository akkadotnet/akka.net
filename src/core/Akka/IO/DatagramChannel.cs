using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Akka.IO
{
    public class DatagramChannel : SocketChannel
    {
        private DatagramChannel(Socket socket) : base(socket)
        {
            
        }

        public static DatagramChannel Open()
        {
            return new DatagramChannel(new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp));
        }

        public override bool IsOpen()
        {
            return true;
        }

        public int Send(ByteBuffer buffer, EndPoint target)
        {
            try
            {
                var data = new byte[buffer.Remaining];
                buffer.Get(data);
                return Socket.SendTo(data, target);
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.WouldBlock)
                {
                    buffer.Flip();
                    return 0;
                }
                throw new IOException(ex.Message, ex);
            }
        }

        public EndPoint Receive(ByteBuffer buffer)
        {
            try
            {
                var ep = Socket.LocalEndPoint;
                var data = new byte[buffer.Remaining];
                var length = Socket.ReceiveFrom(data, ref ep);
                buffer.Put(data, 0, length);
                return ep;
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.WouldBlock)
                    return null;
                throw new IOException(ex.Message, ex);
            }
            
        }
    }
}