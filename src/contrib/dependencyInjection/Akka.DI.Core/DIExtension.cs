//-----------------------------------------------------------------------
// <copyright file="DIExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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

