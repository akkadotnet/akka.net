using Akka;
using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace ChatMessages
{
    [DataContract]
    public class ConnectRequest 
    {
        [DataMember(Order=1)]
        public string Username { get; set; }
    }

    [DataContract]
    public class ConnectResponse 
    {
        [DataMember(Order = 1)]
        public string Message { get; set; }
    }

    [DataContract]
    public class NickRequest 
    {
        [DataMember(Order = 1)]
        public string OldUsername { get; set; }
        [DataMember(Order = 2)]
        public string NewUsername { get; set; }
    }

    [DataContract]
    public class NickResponse 
    {
        [DataMember(Order = 1)]
        public string OldUsername { get; set; }
        [DataMember(Order = 2)]
        public string NewUsername { get; set; }
    }

    [DataContract]
    public class SayRequest 
    {
        [DataMember(Order = 1)]
        public string Username { get; set; }
        [DataMember(Order = 2)]
        public string Text { get; set; }
    }

    [DataContract]
    public class SayResponse 
    {
        [DataMember(Order = 1)]
        public string Username { get; set; }
        [DataMember(Order = 2)]
        public string Text { get; set; }
    }

    [DataContract]
    public class ChannelsRequest 
    {
    }

    [DataContract]
    public class ChannelsResponse 
    {
        [DataMember]
        public ActorRef[] channels { get; set; }
    }

    [DataContract]
    public class Disconnect 
    {
    }
}
