﻿using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Remote
{
    public class EndpointPolicy
    {
    }

    public class Pass : EndpointPolicy
    {
        public Pass(ActorRef endpoint)

        {
            Endpoint = endpoint;
        }

        public ActorRef Endpoint { get; private set; }
    }

    public class Gated : EndpointPolicy
    {
        public Gated(Deadline deadline)
        {
            TimeOfRelease = deadline;
        }

        public Deadline TimeOfRelease { get; private set; }
    }

    public class Deadline
    {
        public Deadline(DateTime when)
        {
            When = when;
        }

        public bool IsOverdue
        {
            get { return DateTime.Now > When; }
        }

        public DateTime When { get; private set; }
    }

    public class Quarantined : EndpointPolicy
    {
        public Quarantined(long uid, Deadline deadline)
        {
            Uid = uid;
            Deadline = deadline;
        }

        public long Uid { get; private set; }

        public Deadline Deadline { get; private set; }
    }

    public class EndpointRegistry
    {
        private readonly Dictionary<Address, ActorRef> addressToReadonly = new Dictionary<Address, ActorRef>();

        private readonly Dictionary<Address, EndpointPolicy> addressToWritable =
            new Dictionary<Address, EndpointPolicy>();

        private readonly Dictionary<ActorRef, Address> readonlyToAddress = new Dictionary<ActorRef, Address>();
        private readonly Dictionary<ActorRef, Address> writableToAddress = new Dictionary<ActorRef, Address>();
        public ActorRef RegisterWritableEndpoint(Address address, ActorRef endpoint)
        {
            EndpointPolicy existing;
            if (addressToWritable.TryGetValue(address, out existing))
            {
                throw new ArgumentException("Attempting to overwrite existing endpoint " + existing + " with " + endpoint);
            }
            addressToWritable.Add(address, new Pass(endpoint));
            writableToAddress.Add(endpoint, address);
            return endpoint;
        }

        public ActorRef RegisterReadOnlyEndpoint(Address address, ActorRef endpoint)
        {
            addressToReadonly.Add(address, endpoint);
            readonlyToAddress.Add(endpoint, address);
            return endpoint;
        }

        /*
 def readOnlyEndpointFor(address: Address): Option[ActorRef] = addressToReadonly.get(address)

    def isWritable(endpoint: ActorRef): Boolean = writableToAddress contains endpoint

    def isReadOnly(endpoint: ActorRef): Boolean = readonlyToAddress contains endpoint

      def writableEndpointWithPolicyFor(address: Address): Option[EndpointPolicy] = addressToWritable.get(address)
*/

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

        public bool IsReadable(ActorRef endpoint)
        {
            return readonlyToAddress.ContainsKey(endpoint);
        }

        public EndpointPolicy WritableEndpointWithPolicyFor(Address address)
        {
            EndpointPolicy tmp;
            if (addressToWritable.TryGetValue(address, out tmp))
            {
                return tmp;
            }
            return null;
        }

        //TODO: port the rest of this class
    }
}