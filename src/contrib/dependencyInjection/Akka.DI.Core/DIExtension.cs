using Akka.Actor;

namespace Akka.DI.Core
{
    /// <summary>
    /// ExtensionIdProvider for the DIExt. Used to Create an instance of DIExt which implements IExtension
    /// </summary>
    public class DIExtension : ExtensionIdProvider<DIExt>
    {
        public static DIExtension DIExtensionProvider = new DIExtension();

        public override DIExt CreateExtension(ExtendedActorSystem system)
        {
            var extension = new DIExt();
            return extension;
        }

    }
}
