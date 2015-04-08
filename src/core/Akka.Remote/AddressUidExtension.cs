using Akka.Actor;
using Akka.Util;

namespace Akka.Remote
{
    /// <summary>
    /// <see cref="IExtension"/> provider for <see cref="AddressUid"/>
    /// </summary>
    public class AddressUidExtension : ExtensionIdProvider<AddressUid>
    {
        public override AddressUid CreateExtension(ExtendedActorSystem system)
        {
            return new AddressUid();
        }

        #region Static methods

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
        public int Uid
        {
            get { return ThreadLocalRandom.Current.Next(); }
        }
    }
}
