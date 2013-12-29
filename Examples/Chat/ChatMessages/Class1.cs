using Pigeon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatMessages
{
    public class ConnectRequest : IMessage
    {
        public string Username { get; set; }
    }

    public class ConnectResponse : IMessage
    {
        public string Message { get; set; }
    }

    public class NickRequest : IMessage
    {
        public string NewUsername { get; set; }
    }

    public class NickResponse : IMessage
    {
        public string OldUsername { get; set; }
        public string NewUsername { get; set; }
    }

    public class SayRequest : IMessage
    {
        public string Username { get; set; }
        public string Text { get; set; }
    }

    public class SayResponse : IMessage
    {
        public string Username { get; set; }
        public string Text { get; set; }
    }

    public class ChannelsRequest : IMessage
    {
    }

    public class ChannelsResponse : IMessage
    {
        public ActorRef[] channels { get; set; }
    }

    public class Disconnect : IMessage
    {
    }
}
