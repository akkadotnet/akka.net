﻿//-----------------------------------------------------------------------
// <copyright file="ActorCell.Children.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Akka.Actor.Internal;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Actor
{
    public partial class ActorCell
    {
        private volatile IChildrenContainer _childrenContainerDoNotCallMeDirectly = EmptyChildrenContainer.Instance;
        private long _nextRandomNameDoNotCallMeDirectly = -1; // Interlocked.Increment automatically adds 1 to this value. Allows us to start from 0.

        /// <summary>
        /// The child container collection, used to house information about all child actors.
        /// </summary>
        public IChildrenContainer ChildrenContainer
        {
            get { return _childrenContainerDoNotCallMeDirectly; }
        }

        private IReadOnlyCollection<IActorRef> Children
        {
            get { return ChildrenContainer.Children; }
        }

        /// <summary>
        /// Attaches a child to the current <see cref="ActorCell"/>.
        /// 
        /// This method is used in the process of starting actors.
        /// </summary>
        /// <param name="props">The <see cref="Props"/> this child actor will use.</param>
        /// <param name="isSystemService">If <c>true</c>, then this actor is a system actor and activates a special initialization path.</param>
        /// <param name="name">The name of the actor being started. Can be <c>null</c>, and if it is we will automatically 
        /// generate a random name for this actor.</param>
        /// <exception cref="InvalidActorNameException">
        /// This exception is thrown if the given <paramref name="name"/> is an invalid actor name.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if a pre-creation serialization occurred.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if the actor tries to create a child while it is terminating or is terminated.
        /// </exception>
        /// <returns>A reference to the initialized child actor.</returns>
        public virtual IActorRef AttachChild(Props props, bool isSystemService, string name = null)
        {
            return MakeChild(props, name == null ? GetRandomActorName() : CheckName(name), true, isSystemService);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="props">TBD</param>
        /// <param name="name">TBD</param>
        /// <exception cref="InvalidActorNameException">
        /// This exception is thrown if the given <paramref name="name"/> is an invalid actor name.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if a pre-creation serialization occurred.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if the actor tries to create a child while it is terminating or is terminated.
        /// </exception>
        /// <returns>TBD</returns>
        public virtual IActorRef ActorOf(Props props, string name = null)
        {
            return ActorOf(props, name, false, false);
        }

        private IActorRef ActorOf(Props props, string name, bool isAsync, bool isSystemService)
        {
            if (name == null)
                name = GetRandomActorName();
            else
                CheckName(name);

            return MakeChild(props, name, isAsync, isSystemService);
        }

        private string GetRandomActorName()
        {
            var id = Interlocked.Increment(ref _nextRandomNameDoNotCallMeDirectly);
            return "$" + id.Base64Encode();
        }

        /// <summary>
        ///     Stops the specified child.
        /// </summary>
        /// <param name="child">The child.</param>
        public void Stop(IActorRef child)
        {
            ChildRestartStats stats;
            if (ChildrenContainer.TryGetByRef(child, out stats))
            {
                var repointableActorRef = child as RepointableActorRef;
                if (repointableActorRef == null || repointableActorRef.IsStarted)
                {
                    while (true)
                    {
                        var oldChildren = ChildrenContainer;
                        var newChildren = oldChildren.ShallDie(child);

                        if (SwapChildrenRefs(oldChildren, newChildren)) break;
                    }
                }
            }
            ((IInternalActorRef)child).Stop();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool SwapChildrenRefs(IChildrenContainer oldChildren, IChildrenContainer newChildren)
        {
            return ReferenceEquals(Interlocked.CompareExchange(ref _childrenContainerDoNotCallMeDirectly, newChildren, oldChildren), oldChildren);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public void ReserveChild(string name)
        {
            while (true)
            {
                var oldChildren = ChildrenContainer;
                var newChildren = oldChildren.Reserve(name);

                if (SwapChildrenRefs(oldChildren, newChildren)) break;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        protected void UnreserveChild(string name)
        {
            while (true)
            {
                var oldChildren = ChildrenContainer;
                var newChildren = oldChildren.Unreserve(name);

                if (SwapChildrenRefs(oldChildren, newChildren)) break;
            }
        }

        /// <summary>
        /// This should only be used privately or when creating the root actor. 
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public ChildRestartStats InitChild(IInternalActorRef actor)
        {
            var name = actor.Path.Name;
            while (true)
            {
                var cc = ChildrenContainer;
                if (cc.TryGetByName(name, out var old))
                {
                    switch (old)
                    {
                        case ChildRestartStats restartStats:
                            return restartStats;
                        case ChildNameReserved _:
                            var crs = new ChildRestartStats(actor);
                            if (SwapChildrenRefs(cc, cc.Add(name, crs)))
                                return crs;
                            break;
                    }
                }
                else return null;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <returns>TBD</returns>
        protected bool SetChildrenTerminationReason(SuspendReason reason)
        {
            while (true)
            {
                if (ChildrenContainer is TerminatingChildrenContainer c)
                {
                    var n = c.CreateCopyWithReason(reason);
                    if (SwapChildrenRefs(c, n)) return true;
                }
                else return false;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected void SetTerminated()
        {
            Interlocked.Exchange(ref _childrenContainerDoNotCallMeDirectly, TerminatedChildrenContainer.Instance);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected bool IsNormal { get { return ChildrenContainer.IsNormal; } }
        /// <summary>
        /// TBD
        /// </summary>
        protected bool IsTerminating { get { return ChildrenContainer.IsTerminating; } }

        private bool IsWaitingForChildren  // This is called isWaitingForChildrenOrNull in AkkaJVM but is used like if returned a bool
        {
            get
            {
                var terminating = ChildrenContainer as TerminatingChildrenContainer;
                return terminating != null && terminating.Reason is SuspendReason.IWaitingForChildren;
            }
        }

        /// <summary>
        ///     Suspends the children.
        /// </summary>
        private void SuspendChildren(List<IActorRef> exceptFor = null)
        {
            if (exceptFor == null)
            {
                foreach (var stats in ChildrenContainer.Stats)
                {
                    var child = stats.Child;
                    child.Suspend();
                }
            }
            else
            {
                foreach (var stats in ChildrenContainer.Stats)
                {
                    var child = stats.Child;
                    if (!exceptFor.Contains(child))
                        child.Suspend();
                }
            }
        }

        /// <summary>
        ///     Resumes the children.
        /// </summary>
        private void ResumeChildren(Exception causedByFailure, IActorRef perpetrator)
        {
            foreach (var stats in ChildrenContainer.Stats)
            {
                var child = stats.Child;
                var cause = (perpetrator != null && child.Equals(perpetrator)) ? causedByFailure : null;
                child.Resume(cause);
            }
        }

        /// <summary>
        /// Tries to get the stats for the child with the specified name. The stats can be either <see cref="ChildNameReserved"/> 
        /// indicating that only a name has been reserved for the child, or a <see cref="ChildRestartStats"/> for a child that 
        /// has been initialized/created.
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="child">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetChildStatsByName(string name, out IChildStats child)   //This is called getChildByName in Akka JVM
        {
            return ChildrenContainer.TryGetByName(name, out child);
        }

        /// <summary>
        /// Tries to get the stats for the child with the specified name. This ignores children for whom only names have been reserved.
        /// </summary>
        private bool TryGetChildRestartStatsByName(string name, out ChildRestartStats child)
        {
            IChildStats stats;
            if (ChildrenContainer.TryGetByName(name, out stats))
            {
                child = stats as ChildRestartStats;
                if (child != null)
                    return true;
            }
            child = null;
            return false;
        }

        /// <summary>
        /// Tries to get the stats for the specified child.
        /// <remarks>Since the child exists <see cref="ChildRestartStats"/> is the only valid <see cref="IChildStats"/>.</remarks>
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="child">TBD</param>
        /// <returns>TBD</returns>
        protected bool TryGetChildStatsByRef(IActorRef actor, out ChildRestartStats child)   //This is called getChildByRef in Akka JVM
        {
            return ChildrenContainer.TryGetByRef(actor, out child);
        }

        // In Akka JVM there is a getAllChildStats here. Use ChildrenRefs.Stats instead

        /// <summary>
        /// Obsolete. Use <see cref="TryGetSingleChild(string, out IInternalActorRef)"/> instead.
        /// </summary>
        /// <param name="name">N/A</param>
        /// <returns>N/A</returns>
        [Obsolete("Use TryGetSingleChild [0.7.1]")]
        public IInternalActorRef GetSingleChild(string name)
        {
            IInternalActorRef child;
            return TryGetSingleChild(name, out child) ? child : ActorRefs.Nobody;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="child">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetSingleChild(string name, out IInternalActorRef child)
        {
            if (name.IndexOf('#') < 0)
            {
                // optimization for the non-uid case
                ChildRestartStats stats;
                if (TryGetChildRestartStatsByName(name, out stats))
                {
                    child = stats.Child;
                    return true;
                }
            }
            else
            {
                var nameAndUid = SplitNameAndUid(name);
                ChildRestartStats stats;
                if (TryGetChildRestartStatsByName(nameAndUid.Name, out stats))
                {
                    var uid = nameAndUid.Uid;
                    if (uid == ActorCell.UndefinedUid || uid == stats.Uid)
                    {
                        child = stats.Child;
                        return true;
                    }
                }
            }
            child = ActorRefs.Nobody;
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="child">TBD</param>
        /// <returns>TBD</returns>
        protected SuspendReason RemoveChildAndGetStateChange(IActorRef child)
        {
            var terminating = ChildrenContainer as TerminatingChildrenContainer;
            if (terminating != null)
            {
                var n = RemoveChild(child);
                if (!(n is TerminatingChildrenContainer))
                    return terminating.Reason;
                else
                    return null;
            }

            RemoveChild(child);
            return null;
        }

        private IChildrenContainer RemoveChild(IActorRef child)
        {
            while (true)
            {
                var oldChildren = ChildrenContainer;
                var newChildren = oldChildren.Remove(child);

                if (SwapChildrenRefs(oldChildren, newChildren)) return newChildren;
            }
        }

        private static string CheckName(string name)
        {
            if (name == null) throw new InvalidActorNameException("Actor name must not be null.");
            if (name.Length == 0) throw new InvalidActorNameException("Actor name must not be empty.");
            if (!ActorPath.IsValidPathElement(name))
            {
                throw new InvalidActorNameException($"Illegal actor name [{name}]. Actor paths MUST: not start with `$`, include only ASCII letters and can only contain these special characters: ${new string(ActorPath.ValidSymbols)}.");
            }
            return name;
        }

        private IInternalActorRef MakeChild(Props props, string name, bool async, bool systemService)
        {
            if (_systemImpl.Settings.SerializeAllCreators && !systemService && !(props.Deploy.Scope is LocalScope))
            {
                var ser = _systemImpl.Serialization;
                if (props.Arguments != null)
                {
                    foreach (var argument in props.Arguments)
                    {
                        if (argument != null && !(argument is INoSerializationVerificationNeeded))
                        {
                            var serializer = ser.FindSerializerFor(argument);
                            var bytes = serializer.ToBinary(argument);
                            var manifestSerializer = serializer as SerializerWithStringManifest;
                            if (manifestSerializer != null)
                            {
                                var manifest = manifestSerializer.Manifest(argument);
                                if (ser.Deserialize(bytes, manifestSerializer.Identifier, manifest) == null)
                                {
                                    throw new ArgumentException($"Pre-creation serialization check failed at [${_self.Path}/{name}]", nameof(name));
                                }
                            }
                            else
                            {
                                if (ser.Deserialize(bytes, serializer.Identifier, argument.GetType().TypeQualifiedName()) == null)
                                {
                                    throw new ArgumentException($"Pre-creation serialization check failed at [${_self.Path}/{name}]", nameof(name));
                                }
                            }
                        }
                    }
                }
            }

            // In case we are currently terminating, fail external attachChild requests
            // (internal calls cannot happen anyway because we are suspended)
            if (ChildrenContainer.IsTerminating)
            {
                throw new InvalidOperationException("Cannot create child while terminating or terminated");
            }
            else
            {
                // this name will either be unreserved or overwritten with a real child below
                ReserveChild(name);
                IInternalActorRef actor;
                try
                {
                    var childPath = new ChildActorPath(Self.Path, name, NewUid());
                    actor = _systemImpl.Provider.ActorOf(_systemImpl, props, _self, childPath,
                        systemService: systemService, deploy: null, lookupDeploy: true, async: async);
                }
                catch
                {
                    //if actor creation failed, unreserve the name
                    UnreserveChild(name);
                    throw;
                }

                if (Mailbox != null && IsFailed)
                {
                    for (var i = 1; i <= Mailbox.SuspendCount(); i++)
                        actor.Suspend();
                }

                //replace the reservation with the real actor
                InitChild(actor);
                actor.Start();
                return actor;
            }
        }
    }
}

