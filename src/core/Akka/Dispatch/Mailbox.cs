//-----------------------------------------------------------------------
// <copyright file="Mailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
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
          var formattedMessage = args.Length == 0 ? message : string.Format(message, args);
          Console.WriteLine("[MAILBOX][{0}][Thread {1:0000}] {2}", DateTime.Now.ToString("o"), Thread.CurrentThread.ManagedThreadId, formattedMessage);
        }

        private volatile ActorCell _actorCell;
        protected MessageDispatcher dispatcher;
        protected ActorCell ActorCell { get { return _actorCell; } }        

        /// <summary>
        ///     Attaches an ActorCell to the Mailbox.
        /// </summary>
        /// <param name="actorCell"></param>
        public virtual void SetActor(ActorCell actorCell)
        {
            _actorCell = actorCell;
        }

        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        ///     Posts the specified envelope to the mailbox.
        /// </summary>
        /// <param name="receiver"></param>
        /// <param name="envelope">The envelope.</param>
        public abstract void Post(IActorRef receiver, Envelope envelope);

        /// <summary>
        ///     Stops this instance.
        /// </summary>
        public abstract void BecomeClosed();

        public abstract bool IsClosed { get; }
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

        private volatile MailboxSuspendStatus _suspendStatus;
        public bool IsSuspended { get { return _suspendStatus != MailboxSuspendStatus.NotSuspended; } }

        public void Suspend()
        {
            Suspend(MailboxSuspendStatus.Supervision);
        }

        public void Resume()
        {
            _suspendStatus = MailboxSuspendStatus.NotSuspended;
            Schedule();
        }

        public void Suspend(MailboxSuspendStatus reason)
        {
            _suspendStatus |= reason;
        }

        public void Resume(MailboxSuspendStatus reason)
        {
            _suspendStatus &= ~reason;
            Schedule();
        }

        protected abstract void Schedule();


        public abstract void CleanUp();

        //TODO: When Mailbox gets SuspendCount, update ActorCell.MakeChild
    }

    [Flags]
    public enum MailboxSuspendStatus
    {
        NotSuspended = 0,
        Supervision = 1,
        AwaitingTask = 2,
    }
}

