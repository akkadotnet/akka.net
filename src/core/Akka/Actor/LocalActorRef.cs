//-----------------------------------------------------------------------
// <copyright file="LocalActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor.Internal;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Util.Internal;

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    public class LocalActorRef : ActorRefWithCell, ILocalRef
    {
        private readonly ActorSystemImpl _system;
        private readonly Props _props;
        private readonly MessageDispatcher _dispatcher;
        private readonly IInternalActorRef _supervisor;
        private readonly ActorPath _path;
        private ActorCell _cell;

        //This mimics what's done in Akka`s construction of an LocalActorRef.
        //The actorCell is created in the overridable newActorCell() during creation of the instance.
        //Since we don't want calls to virtual members in C# we must supply it. 
        //
        //This is the code from Akka:
        //    private[akka] class LocalActorRef private[akka] (
        //        _system: ActorSystemImpl,
        //        _props: Props,
        //        _dispatcher: MessageDispatcher,
        //        _mailboxType: MailboxType,
        //        _supervisor: InternalActorRef,
        //        override val path: ActorPath) extends ActorRefWithCell with LocalRef {
        //      private val actorCell: ActorCell = newActorCell(_system, this, _props, _dispatcher, _supervisor)
        //      actorCell.init(sendSupervise = true, _mailboxType)
        //      ...
        //    }		
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="props">TBD</param>
        /// <param name="dispatcher">TBD</param>
        /// <param name="mailboxType">TBD</param>
        /// <param name="supervisor">TBD</param>
        /// <param name="path">TBD</param>
        public LocalActorRef(ActorSystemImpl system, Props props, MessageDispatcher dispatcher, MailboxType mailboxType, IInternalActorRef supervisor, ActorPath path) 
        {
            _system = system;
            _props = props;
            _dispatcher = dispatcher;
            MailboxType = mailboxType;
            _supervisor = supervisor;
            _path = path;

           /*
            * Safe publication of this class’s fields is guaranteed by Mailbox.SetActor()
            * which is called indirectly from ActorCell.init() (if you’re wondering why
            * this is at all important, remember that under the CLR readonly fields are only
            * frozen at the _end_ of the constructor, but we are publishing "this" before
            * that is reached).
            * This means that the result of NewActorCell needs to be written to the field
            * _cell before we call init and start, since we can start using "this"
            * object from another thread as soon as we run init.
            */
            // ReSharper disable once VirtualMemberCallInContructor 
            _cell = NewActorCell(_system, this, _props, _dispatcher, _supervisor); // _cell needs to be assigned before Init is called. 
            _cell.Init(true, MailboxType);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="self">TBD</param>
        /// <param name="props">TBD</param>
        /// <param name="dispatcher">TBD</param>
        /// <param name="supervisor">TBD</param>
        /// <returns>TBD</returns>
        protected virtual ActorCell NewActorCell(ActorSystemImpl system, IInternalActorRef self, Props props,
            MessageDispatcher dispatcher, IInternalActorRef supervisor)
        {
            return new ActorCell(system, self, props, dispatcher, supervisor);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override ICell Underlying
        {
            get { return _cell; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ActorCell Cell
        {
            get { return _cell; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IActorRefProvider Provider
        {
            get { return _cell.SystemImpl.Provider; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IInternalActorRef Parent
        {
            get { return _cell.Parent; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IEnumerable<IActorRef> Children
        {
            get { return _cell.GetChildren(); }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Start()
        {
            _cell.Start();
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Stop()
        {
            _cell.Stop();
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Suspend()
        {
            _cell.Suspend();
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsLocal
        {
            get { return true; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public override void SendSystemMessage(ISystemMessage message)
        {
            _cell.SendSystemMessage(message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override ActorPath Path
        {
            get { return _path; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected ActorSystem System => _system;

        /// <summary>
        /// TBD
        /// </summary>
        protected Props Props => _props;

        /// <summary>
        /// TBD
        /// </summary>
        protected MessageDispatcher Dispatcher => _dispatcher;

        /// <summary>
        /// TBD
        /// </summary>
        protected IInternalActorRef Supervisor => _supervisor;

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsTerminated => _cell.IsTerminated;

        /// <summary>
        /// TBD
        /// </summary>
        protected MailboxType MailboxType { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="causedByFailure">TBD</param>
        public override void Resume(Exception causedByFailure = null)
        {
            _cell.Resume(causedByFailure);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public override void Restart(Exception cause)
        {
            _cell.Restart(cause);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        protected override void TellInternal(object message, IActorRef sender)
        {
            _cell.SendMessage(sender, message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public override IInternalActorRef GetSingleChild(string name)
        {
            IInternalActorRef child;
            return _cell.TryGetSingleChild(name, out child) ? child : ActorRefs.Nobody;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public override IActorRef GetChild(IEnumerable<string> name)
        {
            var current = (IActorRef)this;
            int index = 0;
            foreach (string element in name)
            {
                var currentLocalActorRef = current as LocalActorRef;
                if (currentLocalActorRef != null)
                {
                    switch (element)
                    {
                        case "..":
                            current = currentLocalActorRef.Parent;
                            break;
                        case "":
                            break;
                        default:
                            current = currentLocalActorRef.GetSingleChild(element);
                            break;
                    }
                }
                else
                {
                    //Current is not a LocalActorRef
                    if (current != null)
                    {
                        var rest = name.Skip(index).ToList();
                        return current.AsInstanceOf<IInternalActorRef>().GetChild(rest);
                    }
                    throw new NotSupportedException("Bug, we should not get here");
                }
                index++;
            }
            return current;
        }
    }
}

