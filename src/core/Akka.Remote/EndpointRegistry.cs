//-----------------------------------------------------------------------
// <copyright file="EndpointRegistry.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Remote
{
    /// <summary>
    /// Not threadsafe - only to be used in HeadActor
    /// </summary>
    internal class EndpointRegistry
    {
        private readonly Dictionary<Address, Tuple<IActorRef, int>> _addressToReadonly = new Dictionary<Address, Tuple<IActorRef, int>>();

        private Dictionary<Address, EndpointManager.EndpointPolicy> _addressToWritable =
            new Dictionary<Address, EndpointManager.EndpointPolicy>();

        private readonly Dictionary<IActorRef, Address> _readonlyToAddress = new Dictionary<IActorRef, Address>();
        private readonly Dictionary<IActorRef, Address> _writableToAddress = new Dictionary<IActorRef, Address>();
        public IActorRef RegisterWritableEndpoint(Address address, IActorRef endpoint, int? uid, int? refuseUid)
        {
            EndpointManager.EndpointPolicy existing;
            _addressToWritable.TryGetValue(address, out existing);

            var pass = existing as EndpointManager.Pass;
            if (pass != null) // if we already have a writable endpoint....
            {
                var e = pass.Endpoint;
                throw new ArgumentException("Attempting to overwrite existing endpoint " + e + " with " + endpoint);
            }

            _addressToWritable.AddOrSet(address, new EndpointManager.Pass(endpoint, uid, refuseUid));
            _writableToAddress.AddOrSet(endpoint, address);
            return endpoint;
        }

        public void RegisterWritableEndpointUid(IActorRef writer, int uid)
        {
            var address = _writableToAddress[writer];
            if (_addressToWritable[address] is EndpointManager.Pass)
            {
                var pass = (EndpointManager.Pass)_addressToWritable[address];
                _addressToWritable[address] = new EndpointManager.Pass(pass.Endpoint, uid, pass.RefuseUid);
            }

            // if the policy is not Pass, then the GotUid might have lost the race with some failure

        }

        public IActorRef RegisterReadOnlyEndpoint(Address address, IActorRef endpoint, int uid)
        {
            _addressToReadonly.Add(address, Tuple.Create(endpoint, uid));
            _readonlyToAddress.Add(endpoint, address);
            return endpoint;
        }

        public void UnregisterEndpoint(IActorRef endpoint)
        {
            if (IsWritable(endpoint))
            {
                var address = _writableToAddress[endpoint];
                var policy = _addressToWritable[address];
                if (policy.IsTombstone)
                {
                    //if there is already a tombstone directive, leave it there
                }
                else
                {
                    _addressToWritable.Remove(address);
                }
                _writableToAddress.Remove(endpoint);
            }
            else if (IsReadOnly(endpoint))
            {
                _addressToReadonly.Remove(_readonlyToAddress[endpoint]);
                _readonlyToAddress.Remove(endpoint);
            }
        }

        public Address AddressForWriter(IActorRef writer)
        {
            // Needs to return null if the key is not in the dictionary, instead of throwing.
            Address value;
            return _writableToAddress.TryGetValue(writer, out value) ? value : null;
        }

        public Tuple<IActorRef, int> ReadOnlyEndpointFor(Address address)
        {
            Tuple<IActorRef, int> tmp;
            if (_addressToReadonly.TryGetValue(address, out tmp))
            {
                return tmp;
            }
            return null;
        }

        public bool IsWritable(IActorRef endpoint)
        {
            return _writableToAddress.ContainsKey(endpoint);
        }

        public bool IsReadOnly(IActorRef endpoint)
        {
            return _readonlyToAddress.ContainsKey(endpoint);
        }

        public bool IsQuarantined(Address address, int uid)
        {
            // timeOfRelease is only used for garbage collection. If an address is still probed, we should report the
            // known fact that it is quarantined.
            var policy = WritableEndpointWithPolicyFor(address) as EndpointManager.Quarantined;
            return policy?.Uid == uid;
        }

        public int? RefuseUid(Address address)
        {
            // timeOfRelease is only used for garbage collection. If an address is still probed, we should report the
            // known fact that it is quarantined.
            var policy = WritableEndpointWithPolicyFor(address);
            var q = policy as EndpointManager.Quarantined;
            var p = policy as EndpointManager.Pass;
            if (q != null) return q.Uid;
            if (p != null) return p.RefuseUid;
            return null;
        }

        public EndpointManager.EndpointPolicy WritableEndpointWithPolicyFor(Address address)
        {
            EndpointManager.EndpointPolicy tmp;
            if (_addressToWritable.TryGetValue(address, out tmp))
            {
                return tmp;
            }
            return null;
        }

        public bool HasWriteableEndpointFor(Address address)
        {
            return WritableEndpointWithPolicyFor(address) != null;
        }

        /// <summary>
        /// Marking an endpoint as failed means that we will not try to connect to the remote system within
        /// the gated period but it is ok for the remote system to try to connect with us (inbound-only.)
        /// </summary>
        public void MarkAsFailed(IActorRef endpoint, Deadline timeOfRelease)
        {
            if (IsWritable(endpoint))
            {
                _addressToWritable.AddOrSet(_writableToAddress[endpoint], new EndpointManager.Gated(timeOfRelease));
                _writableToAddress.Remove(endpoint);
            }
            else if (IsReadOnly(endpoint))
            {
                _addressToReadonly.Remove(_readonlyToAddress[endpoint]);
                _readonlyToAddress.Remove(endpoint);
            }
        }

        public void MarkAsQuarantined(Address address, int uid, Deadline timeOfRelease)
        {
            _addressToWritable.AddOrSet(address, new EndpointManager.Quarantined(uid, timeOfRelease));
        }

        public void RemovePolicy(Address address)
        {
            _addressToWritable.Remove(address);
        }

        public IList<IActorRef> AllEndpoints
        {
            get { return _writableToAddress.Keys.Concat(_readonlyToAddress.Keys).ToList(); }
        }

        public void Prune()
        {
            _addressToWritable = _addressToWritable.Where(
                x => PruneFilterFunction(x.Value)).ToDictionary(key => key.Key, value => value.Value);
        }

        /// <summary>
        /// Internal function used for filtering endpoints that need to be pruned due to non-recovery past their deadlines
        /// </summary>
        private static bool PruneFilterFunction(EndpointManager.EndpointPolicy policy)
        {
            var rValue = true;

            policy.Match()
                .With<EndpointManager.Gated>(g => rValue = g.TimeOfRelease.HasTimeLeft)
                .With<EndpointManager.Quarantined>(q => rValue = q.Deadline.HasTimeLeft)
                .Default(msg => rValue = true);

            return rValue;
        }
    }
}

