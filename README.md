Pigeon
======

Actor based framework inspired by Akka

Getting started
===============

Write your first actor:

    public class Greet : IMessage
    {
        public string Who { get; set; }
    }
    
    public class GreetingActor : UntypedActor
    {
        protected override void OnReceive(IMessage message)
        {
            message.Match()
                .With<Greet>(m => Console.WriteLine("Hello {0}", m.Who));
        }
    }
    
Usage:

    var system = new ActorSystem();
    var greeter = system.ActorOf<GreetingActor>("greeter");
    greeter.Tell(new Greet { Who = "Roger" }, ActorRef.NoSender);

Remoting
========

    //Server Program.CS 
    var system = ActorSystemSignalR.Create("myserver", "http://localhost:8080);
    var greeter = system.ActorOf<GreetingActor>("greeter");
    Console.ReadLine();
    
    //Client Program.CS
    var system = new ActorSystem();
    var greeter = system.ActorSelection("http://localhost:8080/greeter");    
    //pass a message to the remote actor
    greeter.Tell(new Greet { Who = "Roger" }, ActorRef.NoSender);

    
