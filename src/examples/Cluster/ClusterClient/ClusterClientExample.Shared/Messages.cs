using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClusterClientExample.Shared
{
    public abstract class TextMessage
    {
        public string Body { get; }
        public string SenderPath { get; }
        protected TextMessage(string body, string senderPath)
        {
            Body = body;
            SenderPath = senderPath;
        }
    }
    public class ThankYou : TextMessage
    {
        public ThankYou(string body, string senderPath) : base(body, senderPath) { }
    }
    public class YouAreWelcome : TextMessage
    {
        public YouAreWelcome(string body, string senderPath) : base(body, senderPath) { }
    }
}
