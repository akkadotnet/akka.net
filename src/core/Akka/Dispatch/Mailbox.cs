using System;
using System.Diagnostics;
using Akka.Actor;

namespace Akka.Dispatch
{
    /// <summary>
    /// Mailbox base class
    /// </summary>
    public abstract class Mailbox : IDisposable
    {
        /// <summary>
        /// Prints a message tosStandard out if the Compile symbol "MAILBOXDEBUG" has been set.
        /// If the symbol is not set all invocations to this method will be removed by the compiler.
        /// </summary>
        [Conditional("MAILBOXDEBUG")]
        public static void DebugPrint(string message, params object[] args)
        {
            if(args.Length == 0)
                Console.WriteLine("{0}", message);
            else
                Console.WriteLine(message, args);
        }

        private volatile ActorCell _actorCell;
        protected MessageDispatcher dispatcher;
        protected ActorCell ActorCell { get { return _actorCell; } }        

        /// <summary>
        ///     Attaches an ActorCell to the Mailbox.
        /// </summary>
        /// <param name="actorCell"></param>
        public void SetActor(ActorCell actorCell)
        {
            _actorCell = actorCell;
        }

        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public abstract void Dispose();

        /// <summary>
        ///     Posts the specified envelope to the mailbox.
        /// </summary>
        /// <param name="receiver"></param>
        /// <param name="envelope">The envelope.</param>
        public abstract void Post(ActorRef receiver, Envelope envelope);

        /// <summary>
        ///     Stops this instance.
        /// </summary>
        public abstract void BecomeClosed();

        /// <summary>
        ///     Attaches a MessageDispatcher to the Mailbox.
        /// </summary>
        /// <param name="dispatcher">The dispatcher.</param>
        public void Setup(MessageDispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
        }

        /// <summary>
        ///     The has unscheduled messages
        /// </summary>
// ReSharper disable once InconsistentNaming
        protected volatile bool hasUnscheduledMessages;

        internal bool HasUnscheduledMessages
        {
            get
            {
                return hasUnscheduledMessages;
            }
        }


        public void Start()
        {
            status = MailboxStatus.Idle;
            Schedule();
        }

        /// <summary>
        ///     The mailbox status (busy or idle)
        /// </summary>
// ReSharper disable once InconsistentNaming
        protected int status = MailboxStatus.Busy;  //HACK: Initially set the mailbox as busy in order for it not to scheduled until we want it to

        internal int Status
        {
            get
            {
                return status;
            }
        }

        /// <summary>
        ///     Class MailboxStatus.
        /// </summary>
        internal static class MailboxStatus
        {
            /// <summary>
            ///     The idle
            /// </summary>
            public const int Idle = 0;

            /// <summary>
            ///     The busy
            /// </summary>
            public const int Busy = 1;
        }

        protected abstract int GetNumberOfMessages();

        internal bool HasMessages
        {
            get
            {
                return NumberOfMessages > 0;
            }
        }
        internal int NumberOfMessages
        {
            get
            {
                return GetNumberOfMessages();
            }
        }

        protected volatile bool _isSuspended;

        public void Suspend()
        {
            _isSuspended = true;
        }

        public void Resume()
        {
            _isSuspended = false;
            Schedule();
        }

        protected abstract void Schedule();


        public abstract void CleanUp();
    }   
}