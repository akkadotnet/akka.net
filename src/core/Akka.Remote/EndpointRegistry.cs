//-----------------------------------------------------------------------
// <copyright file="EndpointRegistry.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private Dictionary<Address, (int, Deadline)> _addressToRefuseUid = new Dictionary<Address, (int, Deadline)>();
        private readonly Dictionary<Address, (IActorRef, int)> _addressToReadonly = new Dictionary<Address, (IActorRef, int)>();

        private Dictionary<Address, EndpointManager.EndpointPolicy> _addressToWritable =
            new Dictionary<Address, EndpointManager.EndpointPolicy>();

        private readonly Dictionary<IActorRef, Address> _readonlyToAddress = new Dictionary<IActorRef, Address>();
        private readonly Dictionary<IActorRef, Address> _writableToAddress = new Dictionary<IActorRef, Address>();

        /// <summary>
        /// Registers a new writable endpoint with the system.
        /// </summary>
        /// <param name="address">The remote address.</param>>
        /// <param name="endpoint">The local endpoint actor who owns this connection.</param>
        /// <param name="uid">The UID of the remote actor system. Can be <c>null</c>.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="address"/> does not have
        /// an <see cref="EndpointManager.EndpointPolicy"/> of <see cref="EndpointManager.Pass"/>
        /// in the registry.
        /// </exception>
        /// <returns>The <paramref name="endpoint"/> actor reference.</returns>
        public IActorRef RegisterWritableEndpoint(Address address, IActorRef endpoint, int? uid)
        {
            if (_addressToWritable.TryGetValue(address, out var existing))
            {
                if (existing is EndpointManager.Pass pass) // if we already have a writable endpoint....
                {
                    throw new ArgumentException($"Attempting to overwrite existing endpoint {pass.Endpoint} with {endpoint}");
                }
            }

            // note that this overwrites Quarantine marker,
            // but that is ok since we keep the quarantined uid in addressToRefuseUid
            _addressToWritable[address] = new EndpointManager.Pass(endpoint, uid);
            _writableToAddress[endpoint] = address;
            return endpoint;
        }

        /// <summary>
        /// Sets the UID for an existing <see cref="EndpointManager.Pass"/> policy.
        /// </summary>
        /// <param name="remoteAddress">The address of the remote system.</param>
        /// <param name="uid">The UID of the remote system.</param>
        public void RegisterWritableEndpointUid(Address remoteAddress, int uid)
        {
            if (_addressToWritable.TryGetValue(remoteAddress, out var existing))
            {
                if (existing is EndpointManager.Pass pass)
                    _addressToWritable[remoteAddress] = new EndpointManager.Pass(pass.Endpoint, uid);
                // if the policy is not Pass, then the GotUid might have lost the race with some failure
            }
        }

        /// <summary>
        /// Record a "refused" UID for a remote system that has been quarantined.
        /// </summary>
        /// <param name="remoteAddress">The remote address of the quarantined system.</param>
        /// <param name="refuseUid">The refused UID of the remote system.</param>
        /// <param name="timeOfRelease">The timeframe for releasing quarantine.</param>
        public void RegisterWritableEndpointRefuseUid(Address remoteAddress, int refuseUid, Deadline timeOfRelease)
        {
            _addressToRefuseUid[remoteAddress] = (refuseUid, timeOfRelease);
        }

        /// <summary>
        /// Registers a read-only endpoint.
        /// </summary>
        /// <param name="address">The remote address.</param>>
        /// <param name="endpoint">The local endpoint actor who owns this connection.</param>
        /// <param name="uid">The UID of the remote actor system. Can be <c>null</c>.</param>
        /// <returns>The <paramref name="endpoint"/> actor reference.</returns>
        public IActorRef RegisterReadOnlyEndpoint(Address address, IActorRef endpoint, int uid)
        {
            _addressToReadonly[address] = (endpoint, uid);
            _readonlyToAddress[endpoint] = address;
            return endpoint;
        }

        /// <summary>
        /// Unregisters an endpoint from the registry.
        /// </summary>
        /// <param name="endpoint">The actor who owns the endpoint.</param>
        public void UnregisterEndpoint(IActorRef endpoint)
        {
            if (IsWritable(endpoint))
            {
                var address = _writableToAddress[endpoint];
                _addressToWritable.TryGetValue(address, out var policy);
                if (policy != null && policy.IsTombstone)
                {
                    //if there is already a tombstone directive, leave it there
                }
                else
                    _addressToWritable.Remove(address);
                _writableToAddress.Remove(endpoint);
                // leave the refuseUid
            }
            else if (IsReadOnly(endpoint))
            {
                _addressToReadonly.Remove(_readonlyToAddress[endpoint]);
                _readonlyToAddress.Remove(endpoint);
            }
        }

        /// <summary>
        /// Get the endpoint address for the selected endpoint writer actor.
        /// </summary>
        /// <param name="writer">The endpoint writer actor reference.</param>
        /// <returns>The remote system address owned by <paramref name="writer"/>.</returns>
        public Address AddressForWriter(IActorRef writer)
        {
            // Needs to return null if the key is not in the dictionary, instead of throwing.
            _writableToAddress.TryGetValue(writer, out var value);
            return value;
        }

        /// <summary>
        /// Gets a read-only endpoint for the given address, if it exists.
        /// </summary>
        /// <param name="address">The remote address to check.</param>
        /// <returns>A tuple containing the actor reference and the remote system UID, if they exist. Otherwise <c>null</c>.</returns>
        public (IActorRef, int)? ReadOnlyEndpointFor(Address address)
        {
            if (!_addressToReadonly.TryGetValue(address, out var tmp))
                return null;
            
            return tmp;
        }

        /// <summary>
        /// Checks to see if the current endpoint is writable or not.
        /// </summary>
        /// <param name="endpoint">The actor who owns the endpoint.</param>
        /// <returns><c>true</c> is writable, <c>false</c> otherwise.</returns>
        public bool IsWritable(IActorRef endpoint)
        {
            return _writableToAddress.ContainsKey(endpoint);
        }

        /// <summary>
        /// Checks to see if the current endpoint is read-only or not.
        /// </summary>
        /// <param name="endpoint">The actor who owns the endpoint.</param>
        /// <returns><c>true</c> is read-only, <c>false</c> otherwise.</returns>
        public bool IsReadOnly(IActorRef endpoint)
        {
            return _readonlyToAddress.ContainsKey(endpoint);
        }

        /// <summary>
        /// Checks to see if the specified address is already quarantined or not.
        /// </summary>
        /// <param name="address">The address to check.</param>
        /// <param name="uid">The current UID of <paramref name="address"/>.</param>
        /// <returns><c>true</c> if this system is under quarantine with its current UID, <c>false</c> otherwise.</returns>
        public bool IsQuarantined(Address address, int uid)
        {
            // timeOfRelease is only used for garbage collection. If an address is still probed, we should report the
            // known fact that it is quarantined.
            var policy = WritableEndpointWithPolicyFor(address) as EndpointManager.Quarantined;
            switch (policy)
            {
                case EndpointManager.Quarantined q when q.Uid == uid:
                    return true;
                default:
                    if (_addressToRefuseUid.ContainsKey(address))
                    {
                        return _addressToRefuseUid[address].Item1 == uid;
                    }

                    return false;
            }
        }

        /// <summary>
        /// Find the "refused" UID for the specified address, if it exists.
        /// </summary>
        /// <param name="address">The address to check.</param>
        /// <returns>The refused UID if one exists for this address; otherwise <c>null</c>.</returns>
        public int? RefuseUid(Address address)
        {
            // timeOfRelease is only used for garbage collection. If an address is still probed, we should report the
            // known fact that it is quarantined.
            var policy = WritableEndpointWithPolicyFor(address);
            switch (policy)
            {
                case EndpointManager.Quarantined q:
                    return q.Uid;
                default:
                    if (_addressToRefuseUid.ContainsKey(address))
                        return _addressToRefuseUid[address].Item1;
                    return null;
            }
        }

        /// <summary>
        /// Return the writable endpoint policy for the provided remote address, if it exists.
        /// </summary>
        /// <param name="address">The remote address to check.</param>
        /// <returns>The <c>EndpointManager.EndpointPolicy</c> if we have one for this address, <c>null</c> otherwise.</returns>
        public EndpointManager.EndpointPolicy WritableEndpointWithPolicyFor(Address address)
        {
            _addressToWritable.TryGetValue(address, out var tmp);
            return tmp;
        }

        /// <summary>
        /// Check if we have a writable endpoint for the provided address.
        /// </summary>
        /// <param name="address">The address to check.</param>
        /// <returns><c>true</c> if we have a writable</returns>
        public bool HasWritableEndpointFor(Address address)
        {
            var policy = WritableEndpointWithPolicyFor(address);
            return policy is EndpointManager.Pass;
        }

        /// <summary>
        /// Marking an endpoint as failed means that we will not try to connect to the remote system within
        /// the gated period but it is ok for the remote system to try to connect with us (inbound-only.)
        /// </summary>
        /// <param name="endpoint">The endpoint actor.</param>
        /// <param name="timeOfRelease">The time to release the failure policy.</param>
        public void MarkAsFailed(IActorRef endpoint, Deadline timeOfRelease)
        {
            if (IsWritable(endpoint))
            {
                var address = _writableToAddress[endpoint];
                if (_addressToWritable.TryGetValue(address, out var policy))
                {
                    if (policy is EndpointManager.Quarantined)
                    {
                        // don't overwrite Quarantined with Gated
                    }
                    else if (policy is EndpointManager.Pass)
                    {
                        _addressToWritable[address] = new EndpointManager.Gated(timeOfRelease);
                        _writableToAddress.Remove(endpoint);
                    }
                    else if (policy is EndpointManager.Gated)
                    {
                        // already gated
                    }
                }
                else
                {
                    _addressToWritable[address] = new EndpointManager.Gated(timeOfRelease);
                    _writableToAddress.Remove(endpoint);
                }
            }
            else if (IsReadOnly(endpoint))
            {
                _addressToReadonly.Remove(_readonlyToAddress[endpoint]);
                _readonlyToAddress.Remove(endpoint);
            }
        }

        /// <summary>
        /// Mark the current remote address as quarantined.
        /// </summary>
        /// <param name="address">The address to quarantine.</param>
        /// <param name="uid">The UID of the current address.</param>
        /// <param name="timeOfRelease">The timeframe to release the quarantine.</param>
        public void MarkAsQuarantined(Address address, int uid, Deadline timeOfRelease)
        {
            _addressToWritable[address] = new EndpointManager.Quarantined(uid, timeOfRelease);
            _addressToRefuseUid[address] = (uid, timeOfRelease);
        }

        /// <summary>
        /// Remove the policy for the current address.
        /// </summary>
        /// <param name="address">The address to prune policy settings for.</param>
        public void RemovePolicy(Address address)
        {
            _addressToWritable.Remove(address);
        }

        /// <summary>
        /// The list of all current endpoint actors.
        /// </summary>
        public IList<IActorRef> AllEndpoints
        {
            get { return _writableToAddress.Keys.Concat(_readonlyToAddress.Keys).ToList(); }
        }

        /// <summary>
        /// Prune all old endpoints.
        /// </summary>
        public void Prune()
        {
            _addressToWritable = _addressToWritable.Where(x =>
            {
                switch (x.Value)
                {
                    case EndpointManager.Gated g when g.TimeOfRelease.HasTimeLeft:
                    case EndpointManager.Quarantined q when q.Deadline.HasTimeLeft:
                    case EndpointManager.Pass p:
                        return true;
                    default:
                        return false;
                }
            }).ToDictionary(key => key.Key, value => value.Value);

            _addressToRefuseUid = _addressToRefuseUid.Where(x => x.Value.Item2.HasTimeLeft)
                .ToDictionary(x => x.Key, x => x.Value);
        }
    }
}

