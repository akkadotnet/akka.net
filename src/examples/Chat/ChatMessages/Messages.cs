using Akka.Actor;

namespace ChatMessages
{
    public class ConnectRequest 
    {
        public string Username { get; set; }
    }

    public class ConnectResponse 
    {       
        public string Message { get; set; }
    }

    public class NickRequest 
    {
        public string OldUsername { get; set; }
        public string NewUsername { get; set; }
    }

    public class NickResponse 
    {
        public string OldUsername { get; set; }
        public string NewUsername { get; set; }
    }

    public class SayRequest 
    {
        public string Username { get; set; }
        public string Text { get; set; }
    }

    public class SayResponse 
    {
        public string Username { get; set; }
        public string Text { get; set; }
    }

    public class ChannelsRequest 
    {
    }

    public class ChannelsResponse 
    {
        public IActorRef[] channels { get; set; }
    }

    public class Disconnect 
    {
    }
}
