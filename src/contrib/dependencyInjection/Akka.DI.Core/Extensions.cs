using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.DI.Core
{
    public static class Extensions
    {
        public static void ActorOf<TActor>(this ActorSystem system, string Name) where TActor : ActorBase
        {
            system.ActorOf(DIExtension.DIExtensionProvider.Get(system).Props(typeof(TActor).Name), Name);
        }
        public static Type GetTypeValue(this string typeName)
        {
            var firstTry = Type.GetType(typeName);
            Func<Type> searchForType = () =>
            {
                return
                AppDomain.
                    CurrentDomain.
                    GetAssemblies().
                    SelectMany(x => x.GetTypes()).
                    Where(t => t.Name.Equals(typeName)).
                    FirstOrDefault();
            };
            return firstTry ?? searchForType();
        }
    }
}
