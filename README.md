# Pigeon

*Pre alpha stage, do not use in production.*

###High performance Actor Model framework.
Pigeon is an Actor Model framework for the .NET platform based on the concepts and API found in the Java/Scala framework AKKA.


#####Throughput
The current implementation of Pigeon can process around **8.5 million messages/second** on a 4 core 2.8ghz laptop.

Akka does 50 million messages/second on a 48 core server, see http://bit.ly/1hpNS3l




## Getting started
Write your first actor:
```csharp
public class Greet
{
    public string Who { get; set; }
}

public class GreetingActor : UntypedActor
{
    protected override void OnReceive(object message)
    {
        Pattern.Match(message)
            .With<Greet>(m => Console.WriteLine("Hello {0}", m.Who));
    }
}
```
Usage:
```csharp
var system = new ActorSystem();
var greeter = system.ActorOf<GreetingActor>("greeter");
greeter.Tell(new Greet { Who = "Roger" });
```
##Remoting
Server:
```csharp
var system = ActorSystemSignalR.Create("myserver", "http://localhost:8080);
var greeter = system.ActorOf<GreetingActor>("greeter");
Console.ReadLine();
```
Client:
```csharp
var system = new ActorSystem();
var greeter = system.ActorSelection("http://localhost:8080/greeter");    
//pass a message to the remote actor
greeter.Tell(new Greet { Who = "Roger" });
```
    
##Code Hotswap
```csharp
public class GreetingActor : UntypedActor
{
    protected override void OnReceive(object message)
    {
        Pattern.Match(message)
            .With<Greet>(m => {
                Console.WriteLine("Hello {0}", m.Who);
                //this could also be a lambda
                Become(OtherReceive);
            });
    }
    
    void OtherReceive(object message)
    {
        Pattern.Match(message)
            .With<Greet>(m => {
                Console.WriteLine("You already said hello!");
                //Unbecome() to revert to old behavior
            });
    }
}
```

##Supervision
```csharp
public class MyActor : UntypedActor
{
    private ActorRef logger = Context.ActorOf<LogActor>();

    // if any child, e.g. the logger above. throws an exception
    // apply the rules below
    // e.g. Restart the child if 10 exceptions occur in 30 seconds or less
    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(
            maxNumberOfRetries: 10, 
            duration: TimeSpan.FromSeconds(30), 
            decider: x =>
            {
                if (x is ArithmeticException)
                    return Directive.Resume;
                if (x is NotSupportedException)
                    return Directive.Stop;

                return Directive.Restart;
            });
    }
    
    ...
}
```
