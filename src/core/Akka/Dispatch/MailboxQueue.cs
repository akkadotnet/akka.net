using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Dispatch
{
    public interface MailboxQueue
    {
        void Enqueue(Envelope envelope);
        bool HasMessages { get; }
        int Count { get; }
        bool TryDequeue(out Envelope envelope);
    }
}
