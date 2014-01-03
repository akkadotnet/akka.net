# Pigeon
Actor based framework inspired by Akka

## Getting started
Write your first actor:
```csharp
public class Greet
{
    public string Who { get; set; }
}

public class GreetingActor : UntypedActor
{
    protected override void OnReceive(IMessage message)
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
    protected override void OnReceive(IMessage message)
    {
        Pattern.Match(message)
            .With<Greet>(m => {
                Console.WriteLine("Hello {0}", m.Who);
                //this could also be a lambda
                Become(OtherReceive);
            });
    }
    
    void OtherReceive(IMessage message)
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
