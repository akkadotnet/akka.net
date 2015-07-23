//-----------------------------------------------------------------------
// <copyright file="Actors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

﻿using Akka.Actor;
﻿using Akka.DI.Core;
﻿using Akka.Routing;
using System;
public class AnotherMessage
{
    public string Name { get; set; }
    public int Id { get; set; }

    public override string ToString()
    {
        return string.Format("{0} {1}", Id, Name);
    }


}
public class TypedActorMessage : IConsistentHashable
{
    public string Name { get; set; }
    public int Id { get; set; }

    public override string ToString()
    {
        return string.Format("{0} {1}", Id, Name);
    }

    public object ConsistentHashKey
    {
        get { return Id; }
    }
}
public class TypedWorker : TypedActor, IHandle<TypedActorMessage>, IHandle<AnotherMessage>
{
    public TypedWorker()
    {
        //
        Console.WriteLine("Created {0}", Guid.NewGuid().ToString());
    }

    public void Handle(TypedActorMessage message)
    {
        Console.WriteLine("{0} received {1}", Self.Path.Name, message);
    }


    public void Handle(AnotherMessage message)
    {

        Console.WriteLine("{0} received other {1}", Self.Path.Name, message);
        Console.WriteLine("TypedWorker - {0} received other {1}", Self.Path.Name, message);
    }
}
/*TODO: this class is not used*/public class TypedParentWorker : TypedActor, IHandle<TypedActorMessage>, IHandle<AnotherMessage>
{
    public TypedParentWorker()
    {
        //
        Console.WriteLine("Created {0}", Guid.NewGuid().ToString());
    }

    public void Handle(TypedActorMessage message)
    {
        Console.WriteLine("TypedParentWorker - {0} received {1}", Self.Path.Name, message);
        var producer = Context.System.GetExtension<DIExt>();
        Context.DI().ActorOf<TypedActor>().Tell(message);
    }


    public void Handle(AnotherMessage message)
    {
        Console.WriteLine("TypedParentWorker - {0} received other {1}", Self.Path.Name, message);
    }
}

