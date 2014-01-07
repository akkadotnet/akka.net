using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Pigeon.Actor
{
    public abstract class Mailbox
    {
        public Action<Envelope> OnNext { get; set; }
        public abstract void Post(Envelope message);
    }

    public class ActionBlockMailbox : Mailbox
    {
        private BufferBlock<Envelope> systemMessages = new BufferBlock<Envelope>();
        private BufferBlock<Envelope> userMessages = new BufferBlock<Envelope>();
        private ActionBlock<Envelope> actionBlock ;

        public ActionBlockMailbox()
        {
            Subscribe();
        }

        private void Subscribe()
        {
            actionBlock = new ActionBlock<Envelope>((Action<Envelope>)OnNextWrapper, new ExecutionDataflowBlockOptions()
            {
                MaxDegreeOfParallelism = 1,
                MaxMessagesPerTask = 1,
                TaskScheduler = TaskScheduler.Default,
                SingleProducerConstrained = false,
            });

            systemMessages.LinkTo(actionBlock, new DataflowLinkOptions()
            {
                Append = false,                
            });
            userMessages.LinkTo(actionBlock, new DataflowLinkOptions()
            {
                Append = true,
            });
        }

        private void OnNextWrapper(Envelope message)
        {
            this.OnNext(message);
        }

        public override void Post(Envelope message)
        {
            if (message.Payload is SystemMessage)
            {
                systemMessages.Post(message);
            }
            else
            {
                userMessages.Post(message);
            }
        }
    }

    public class BufferBlockMailbox : Mailbox
    {
        private BufferBlock<Envelope> bufferblock = new BufferBlock<Envelope>(new ExecutionDataflowBlockOptions()
        {
            MaxDegreeOfParallelism = 1,
            MaxMessagesPerTask = 1,
            BoundedCapacity = 100000000,
            TaskScheduler = TaskScheduler.Default,
        });

        public BufferBlockMailbox()
        {
            Subscribe();
        }

        private async void Subscribe()
        {
            var message = await bufferblock.ReceiveAsync();
            OnNextWrapper(message);
            await Task.Yield();
            Subscribe();
        }

        private void OnNextWrapper(Envelope message)
        {
            this.OnNext(message);
        }

        public override void Post(Envelope message)
        {
            bufferblock.Post(message);
        }
    }

    public class ObservableBufferBlockMailbox : Mailbox
    {
        private BufferBlock<Envelope> bufferblock = new BufferBlock<Envelope>(new ExecutionDataflowBlockOptions()
        {
            MaxDegreeOfParallelism = 1,
            MaxMessagesPerTask = 1,
            BoundedCapacity = 100000,
            TaskScheduler = TaskScheduler.Default,
        });

        public ObservableBufferBlockMailbox()
        {
            bufferblock.AsObservable().Subscribe(OnNextWrapper);
        }

        private void OnNextWrapper(Envelope message)
        {
            this.OnNext(message);
        }

        public override void Post(Envelope message)
        {
            bufferblock.Post(message);
        }
    }

    public class RxSubjectMailbox : Mailbox
    {
        private Subject<Envelope> subject = new Subject<Envelope>();

        public RxSubjectMailbox()
        {
            subject.Subscribe(OnNextWrapper);
        }

        private void OnNextWrapper(Envelope message)
        {
            this.OnNext(message);
        }

        public override void Post(Envelope message)
        {
            subject.OnNext(message);
        }
    }

    /// <summary>
    /// For explorative reasons only
    /// </summary>
    public class ConcurrentQueueMailbox : Mailbox
    {
        private System.Collections.Concurrent.ConcurrentQueue<Envelope> queue = new System.Collections.Concurrent.ConcurrentQueue<Envelope>();

        public ConcurrentQueueMailbox()
        {
            WaitCallback handler = null;
            handler = new WaitCallback(_ =>
            {
                Envelope message = null;
                if (queue.TryDequeue(out message))
                {
                    this.OnNext(message);
                }
                ThreadPool.QueueUserWorkItem(handler);
            });
            System.Threading.ThreadPool.QueueUserWorkItem(handler);
        }

        public override void Post(Envelope message)
        {
            queue.Enqueue(message);
        }
    }
}
