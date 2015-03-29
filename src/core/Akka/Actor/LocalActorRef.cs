using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor.Internals;
using Akka.Dispatch;
using Akka.Util.Internal;

namespace Akka.Actor
{
    public class LocalActorRef : ActorRefWithCell, LocalRef
    {
        private readonly ActorSystem _system;
        private readonly Props _props;
        private readonly MessageDispatcher _dispatcher;
        private readonly Func<Mailbox> _createMailbox;
        private readonly InternalActorRef _supervisor;
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
        public LocalActorRef(ActorSystemImpl system, Props props, MessageDispatcher dispatcher, Func<Mailbox> createMailbox, InternalActorRef supervisor, ActorPath path) //TODO: switch from  Func<Mailbox> createMailbox to MailboxType mailboxType      
            : this(system, props, dispatcher, createMailbox, supervisor, path, self =>
            {
                var cell= new ActorCell(system, self, props, dispatcher, supervisor);
                cell.Init(sendSupervise: true, createMailbox: createMailbox);
                return cell;
            })
        {
            //Intentionally left blank

        }

        /// <summary>
        /// Inheritors should only call this constructor
        /// </summary>
        internal protected  LocalActorRef(ActorSystem system, Props props, MessageDispatcher dispatcher, Func<Mailbox> createMailbox, InternalActorRef supervisor, ActorPath path, Func<LocalActorRef, ActorCell> createActorCell) //TODO: switch from  Func<Mailbox> createMailbox to MailboxType mailboxType      
        {
            _system = system;
            _props = props;
            _dispatcher = dispatcher;
            _createMailbox = createMailbox;
            _supervisor = supervisor;
            _path = path;
            _cell = createActorCell(this);
        }


        public override Cell Underlying
        {
            get { return _cell; }
        }

        public ActorCell Cell
        {
            get { return _cell; }
        }

        public override ActorRefProvider Provider
        {
            get { return _cell.SystemImpl.Provider; }
        }

        public override InternalActorRef Parent
        {
            get { return _cell.Parent; }
        }

        public override IEnumerable<ActorRef> Children
        {
            get { return _cell.GetChildren(); }
        }

        public override void Start()
        {
            _cell.Start();
        }

        public override void Stop()
        {
            _cell.Stop();
        }

        public override void Suspend()
        {
            _cell.Suspend();
        }

        public override bool IsLocal
        {
            get { return true; }
        }

        public override ActorPath Path
        {
            get { return _path; }
        }

        protected ActorSystem System{get { return _system; }}

        protected Props Props{get { return _props; }}

        protected MessageDispatcher Dispatcher{get { return _dispatcher; }}

        protected InternalActorRef Supervisor{get { return _supervisor; }}

        public override bool IsTerminated { get { return _cell.IsTerminated; } }

        protected Func<Mailbox> CreateMailbox
        {
            get { return _createMailbox; }
        }

        public override void Resume(Exception causedByFailure = null)
        {
            _cell.Resume(causedByFailure);
        }

        public override void Restart(Exception cause)
        {
            _cell.Restart(cause);
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            _cell.Post(sender, message);
        }

        public override InternalActorRef GetSingleChild(string name)
        {
            InternalActorRef child;
            return _cell.TryGetSingleChild(name, out child) ? child : ActorRefs.Nobody;
        }

        public override ActorRef GetChild(IEnumerable<string> name)
        {
            var current = (ActorRef) this;
            int index = 0;
            foreach (string element in name)
            {
                var currentLocalActorRef = current as LocalActorRef;
                if(currentLocalActorRef!=null)
                {
                    switch(element)
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
                        return current.AsInstanceOf<InternalActorRef>().GetChild(rest);
                    }
                    throw new NotSupportedException("Bug, we should not get here");
                }
                index++;
            }
            return current;
        }
    }
}