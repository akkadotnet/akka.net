---
layout: wiki
title: Getting started
---
# Getting started with Akka.NET

This tutorial is intended to give an introduction to using Akka.NET by creating a simple greeter actor using C#.

## Set up your project

Start visual studio and create a new C# Console Application.
Once we have our console application, we need to open up the Package Manager Console and type:

```PM
PM> Install-Package Akka
```

Then we need to add the relevant using statements:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//Add these two lines
using Akka;
using Akka.Actor;

namespace ConsoleApplication11
{
    class Program
    {
        static void Main(string[] args)
        {
        }
    }
}
```

## Create your first actor

First, we need to create a message type that our actor will respond to:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Akka;
using Akka.Actor;

namespace ConsoleApplication11
{
    //Create an (immutable) message type that your actor will respond to
    public class Greet
    {
        public Greet(string who)
        {
            Who = who;
        }
        public string Who { get;private set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
        }
    }
}
```

Once we have the message type, we can create our Actor:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Akka;
using Akka.Actor;

namespace ConsoleApplication11
{
    public class Greet
    {
        public Greet(string who)
        {
            Who = who;
        }
        public string Who { get;private set; }
    }

    // Create the actor class
    public class GreetingActor : ReceiveActor
    {
        public GreetingActor()
        {
            // Tell the actor to respond 
            // to the Greet message
            Receive<Greet>(greet => 
               Console.WriteLine("Hello {0}", greet.Who));
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
        }
    }
}
```

Now it's time to consume our actor, we do so by creating an `ActorSystem` and calling `ActorOf`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Akka;
using Akka.Actor;

namespace ConsoleApplication11
{
    public class Greet
    {
        public Greet(string who)
        {
            Who = who;
        }
        public string Who { get;private set; }
    }

    public class GreetingActor : ReceiveActor
    {
        public GreetingActor()
        {
            Receive<Greet>(greet => 
               Console.WriteLine("Hello {0}", greet.Who));
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            //create a new actor system (a container for your actors)
            var system = ActorSystem.Create("MySystem");
            //create your actor and get a reference to it.
            //this will be an "ActorRef", which is not a 
            //reference to the actual actor instance
            //but rather a client or proxy to it
            var greeter = system.ActorOf<GreetingActor>("greeter");
            //send a message to the actor
            greeter.Tell(new Greet("World"));

            //this prevents the app from exiting
            //Before the async work is done
            Console.ReadLine();
        }
    }
}
```

That is it, your actor is now ready to consume messages sent from any number of calling threads.