---
layout: wiki
title: Hotswap
---
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