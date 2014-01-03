using Pigeon.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Pigeon.Actor
{
    public abstract class Mailbox
    {
        public Action<Message> OnNext { get; set; }
        public abstract void Post(Message message);
    }

    public class BufferBlockMailbox : Mailbox
    {
        private BufferBlock<Message> bufferblock = new BufferBlock<Message>(new ExecutionDataflowBlockOptions()
        {
            MaxDegreeOfParallelism = 1,
            MaxMessagesPerTask = 1,
            BoundedCapacity = 100000,
            TaskScheduler = TaskScheduler.Default,
        });

        public BufferBlockMailbox()
        {
            bufferblock.AsObservable().Subscribe(OnNextWrapper);
        }

        private void OnNextWrapper(Message message)
        {
            this.OnNext(message);
        }

        public override void Post(Message message)
        {
            bufferblock.Post(message);
        }
    }
}
