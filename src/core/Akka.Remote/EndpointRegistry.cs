using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Remote
{
    /// <summary>
    /// Not threadsafe - only to be used in HeadActor
    /// </summary>
    internal class EndpointRegistry
    {
        private readonly Dictionary<Address, ActorRef> addressToReadonly = new Dictionary<Address, ActorRef>();

        private Dictionary<Address, EndpointManager.EndpointPolicy> addressToWritable =
            new Dictionary<Address, EndpointManager.EndpointPolicy>();

        private readonly Dictionary<ActorRef, Address> readonlyToAddress = new Dictionary<ActorRef, Address>();
        private readonly Dictionary<ActorRef, Address> writableToAddress = new Dictionary<ActorRef, Address>();
        public ActorRef RegisterWritableEndpoint(Address address, ActorRef endpoint, int? uid = null)
        {
            EndpointManager.EndpointPolicy existing;
            if (addressToWritable.TryGetValue(address, out existing))
            {
                var gated = existing as EndpointManager.Gated;
                if(gated != null && !gated.TimeOfRelease.IsOverdue) //don't throw if the prune timer didn't get a chance to run first
                    throw new ArgumentException("Attempting to overwrite existing endpoint " + existing + " with " + endpoint);
            }
            addressToWritable.AddOrSet(address, new EndpointManager.Pass(endpoint, uid));
            writableToAddress.AddOrSet(endpoint, address);
            return endpoint;
        }

        public void RegisterWritableEndpointUid(ActorRef writer, int uid)
        {
            var address = writableToAddress[writer];
            if (addressToWritable[address] is EndpointManager.Pass)
            {
                var pass = (EndpointManager.Pass) addressToWritable[address];
                addressToWritable[address] = new EndpointManager.Pass(pass.Endpoint, uid);
            }
        }

        public ActorRef RegisterReadOnlyEndpoint(Address address, ActorRef endpoint)
        {
            addressToReadonly.Add(address, endpoint);
            readonlyToAddress.Add(endpoint, address);
            return endpoint;
        }

        public void UnregisterEndpoint(ActorRef endpoint)
        {
            if (IsWritable(endpoint))
            {
                var address = writableToAddress[endpoint];
                if (addressToWritable[address] is EndpointManager.EndpointPolicy)
                {
                    var policy = addressToWritable[address];
                    //if there is already a tombestone directive, leave it there
                    //otherwise, remove this address from the writeable address range
                    if (!policy.IsTombstone)
                    {
                        addressToWritable.Remove(address);
                    }
                }
                writableToAddress.Remove(endpoint);
            }
            else if(IsReadOnly(endpoint))
            {
                addressToReadonly.Remove(readonlyToAddress[endpoint]);
                readonlyToAddress.Remove(endpoint);
            }
        }

        public ActorRef ReadOnlyEndpointFor(Address address)
        {
            ActorRef tmp;
            if (addressToReadonly.TryGetValue(address, out tmp))
            {
                return tmp;
            }
            return null;
        }

        public bool IsWritable(ActorRef endpoint)
        {
            return writableToAddress.ContainsKey(endpoint);
        }

        public bool IsReadOnly(ActorRef endpoint)
        {
            return readonlyToAddress.ContainsKey(endpoint);
        }

        public bool IsQuarantined(Address address, int uid)
        {
            var rvalue = false;
            WritableEndpointWithPolicyFor(address).Match()
                .With<EndpointManager.Quarantined>(q =>
                {
                    if (q.Uid == uid)
                        rvalue = q.Deadline.HasTimeLeft;
                })
                .Default(msg => rvalue = false);

            return rvalue;
        }

        public EndpointManager.EndpointPolicy WritableEndpointWithPolicyFor(Address address)
        {
            EndpointManager.EndpointPolicy tmp;
            if (addressToWritable.TryGetValue(address, out tmp))
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
        public void MarkAsFailed(ActorRef endpoint, Deadline timeOfRelease)
        {
            if (IsWritable(endpoint))
            {
                addressToWritable.AddOrSet(writableToAddress[endpoint], new EndpointManager.Gated(timeOfRelease));
                writableToAddress.Remove(endpoint);
            }
            else if (IsReadOnly(endpoint))
            {
                addressToReadonly.Remove(readonlyToAddress[endpoint]);
                readonlyToAddress.Remove(endpoint);
            }
        }

        public void MarkAsQuarantined(Address address, int uid, Deadline timeOfRelease)
        {
            addressToWritable.AddOrSet(address, new EndpointManager.Quarantined(uid, timeOfRelease));
        }

        public void RemovePolicy(Address address)
        {
            addressToWritable.Remove(address);
        }

        public IList<ActorRef> AllEndpoints
        {
            get { return writableToAddress.Keys.Concat(readonlyToAddress.Keys).ToList(); }
        }

        public void Prune()
        {
            addressToWritable = addressToWritable.Where(
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