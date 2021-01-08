//-----------------------------------------------------------------------
// <copyright file="AddressUidExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Util;

namespace Akka.Remote
{
    /// <summary>
    /// <see cref="IExtension"/> provider for <see cref="AddressUid"/>
    /// </summary>
    public class AddressUidExtension : ExtensionIdProvider<AddressUid>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override AddressUid CreateExtension(ExtendedActorSystem system)
        {
            return new AddressUid();
        }

        #region Static methods

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static int Uid(ActorSystem system)
        {
            return system.WithExtension<AddressUid, AddressUidExtension>().Uid;
        }

        #endregion
    }

    /// <summary>
    /// Extension that holds a UID that is assigned as a random 'Int'.
    /// 
    /// The UID is intended to be used together with an <see cref="Address"/> to be
    /// able to distinguish restarted actor system using the same host and port.
    /// </summary>
    public class AddressUid : IExtension
    {
        /// <summary>
        /// The random unique identifier for this incarnation of the ActorSystem.
        /// </summary>
        public readonly int Uid = ThreadLocalRandom.Current.Next();
    }
}

