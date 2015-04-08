//-----------------------------------------------------------------------
// <copyright file="UnityDependencyResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DI.Core;
using Microsoft.Practices.Unity;

namespace Akka.DI.Unity
{
    public class UnityDependencyResolver : IDependencyResolver
    {
	    private IUnityContainer container;
	    private ConcurrentDictionary<string, Type> typeCache;
	    private ActorSystem system;

	    public UnityDependencyResolver(IUnityContainer container, ActorSystem system)
		{
			if (system == null) throw new ArgumentNullException("system");
            if (container == null) throw new ArgumentNullException("container");
            this.container = container;
		    typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
		    this.system = system;
            this.system.AddDependencyResolver(this);
		}

	    public Type GetType(string actorName)
	    {
		    typeCache.TryAdd(actorName, actorName.GetTypeValue());

            return typeCache[actorName];
	    }

	    public Func<ActorBase> CreateActorFactory(string actorName)
	    {
			return () =>
			{
				var actorType = GetType(actorName);
				return (ActorBase)container.Resolve(actorType);
			};
	    }

	    public Props Create<TActor>() where TActor : ActorBase
	    {
		    return system.GetExtension<DIExt>().Props(typeof(TActor).Name);
	    }

	    public void Release(ActorBase actor)
	    {
		    container.Teardown(actor);
	    }
    }

	internal static class Extensions
    {
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
