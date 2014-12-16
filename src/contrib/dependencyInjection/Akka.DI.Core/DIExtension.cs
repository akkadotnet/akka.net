using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DI.Core
{
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
