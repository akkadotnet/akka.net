Using Receive
=============

### Inherit from ReceiveActor 

In order to use the `Receive()` method inside an actor the actor must inherit from `ReceiveActor`.

```c#
private class MyActor : ReceiveActor
{
}
```

Inside the constructor, add a call to `Receive<T>(Action<T> handler)` for every type of message you want to handle:

```c#
private class MyActor : ReceiveActor
{
  public MyActor()
  {
    Receive<string>(s => Console.WriteLine("Received string: " + s)); //1
    Receive<int>(i => Console.WriteLine("Received integer: " + i));   //2
  }
}
```

Whenever a message of typ `string` is sent to **MyActor** the first handler is invoked and for messages of type `int` the second handler is used.

### Handler priority
If more than one handler matches, the one that appears first is used, and the others are not called.

```c#
Receive<string>(s => Console.WriteLine("Received string: " + )s);      //1
Receive<string>(s => Console.WriteLine("Also received string: " + s)); //2
Receive<object>(o => Console.WriteLine("Received object: " + o));      //3
```
**Example**: The actor receives a message of type `string`. Only the first handler is invoked, even though all three handlers can handle that message.

### Using predicates
By specifying a predicate, you can choose which messages to handle.
```c#
Receive<string>(s => s.Length>5, s => Console.WriteLine("Received string: " + s);
```
The handler above will only be invoked if the length of the string is greater than 20.

If the predicate do not match, the next matching handler will be used.

```c#
Receive<string>(s => s.Length>5, s => Console.WriteLine("1: " + s));    //1
Receive<string>(s => s.Length>2, s => Console.WriteLine("2: " + s));    //2
Receive<string>(s => Console.WriteLine("3: " + s));                     //3
```
**Example**: The actor receives the message "123456". Since the length of is 6, the predicate specified for the first handler will return true, and the first handler will be invoked resulting in "1: 123456" being written to the console.

**Note** that even though the predicate for the second handler matches, and that the third handler matches all messages of type `string` only the first handler is invoked.

**Example**: If the actor receives the message "1234", then "2: 1234" will be written to the console.
**Example**: If the actor receives the message "12", then "3: 12" will be written on the console.

#### Predicates position
Predicates can be specified *before* the action handler or *after*. These two declarations are equivalent:
```c#
Receive<string>(s => s.Length>5, s => Console.WriteLine("Received string: " + s));
Receive<string>(s => Console.WriteLine("Received string: " + s, s => s.Length>5));
```

### Receive using Funcs
More complex handlers can be specified using the `Receive<T>(Func<T,bool> handler)` overload. These are invoked if the message is of the specified type. If the func returns `true`, the message is considered handled, and no more handlers will be invoked.
```c#
Receive<string>(s => 
  { 
    if(s.Length>5)
    {
      Console.WriteLine("1: " + s);
      return true;
    }
    return false;
  });
Receive<string>(s => Console.WriteLine("2: " + s);
```

**Example**: The actor receives the message "123". Since it's a `string`, the first handler is invoked. The length is only 3 so the if clause will be false and `false` is returned. Since `false` was returned the next matching handler will be invoked, and "2: 123" will be written to the console.
**Example**: The actor receives the message "123456". Since it's a `string`, the first handler is invoked. The length is greater than 5 so the if body will be called, and "1: 123456" will be written to the console. The handler returns `true` and therefore no more handlers will be invoked.

### Unmatched messages
If the actor receives a message for which no handler matches, the unhandled message is published to the `EventStream` wrapped in an `UnhandledMessage`. To change this behavior override `Unhandled(object message)`
```c#
protected override void Unhandled(object message)
{
  //Do something with the message.
}
```

Another option is to add a handler last that matches all messages, using `ReceiveAny()`.
### ReceiveAny
To catch messages of any type the `ReceiveAny(Action<object> handler)` overload can be specified.
```c#
Receive<string>(s => Console.WriteLine("Received string: " + s);
ReceiveAny(o => Console.WriteLine("Received object: " + o);
```

Since it handles everything, it must be specified last. Specifying handlers it after will cause an exception.
```c#
ReceiveAny(o => Console.WriteLine("Received object: " + o);
Receive<string>(s => Console.WriteLine("Received string: " + s);  //This will cause an exception
```

**Note** that `Receive<object>(Action<object> handler)` behaves the same as `ReceiveAny()` as it catches all messages. These two are equivalent:
```c#
ReceiveAny(o => Console.WriteLine("Received object: " + o);
Receive<object>(0 => Console.WriteLine("Received object: " + o); 
```

###Non generic overloads
`Receive` has non generic overloads:
```c#
Receive(typeof(string), obj => Console.WriteLine(obj.ToString()) );
```
Predicates can go before or after the handler:
```c#
Receive(typeof(string), obj=> ((string) obj).Length>5, obj => Console.WriteLine(obj.ToString()) );
Receive(typeof(string), obj => Console.WriteLine(obj.ToString()), obj=> ((string) obj).Length>5 );
```
And the non generic Func
```c#
Receive(typeof(string), obj => 
  { 
    var s = (string) obj;
    if(s.Length>5)
    {
      Console.WriteLine("1: " + s);
      return true;
    }
    return false;
  });
```

###Become
You can switch the handler at runtime using `Become()` which replaces the current handler with a new one.
```c#
public class MoodActor : ReceiveActor
{
  public MoodActor()
  {
    Receive<string>(s => s == "Mood?", _ => Sender.Tell("I'm neutral"));
    Receive<string>(s => s == "Happy", _ => Become(Happy));
    Receive<string>(s => s == "Angry", _ => Become(Angry));
  }

  private void Happy()
  {
    Receive<string>(s => s == "Mood?", _ => Sender.Tell("I'm happy"));
    Receive<string>(s => s == "Happy", _ => Sender.Tell("I'm already happy!", Self));
    Receive<string>(s => s == "Angry", _ => Become(Angry));
  }

  private void Angry()
  {
    Receive<string>(s => s == "Mood?", _ => Sender.Tell("I'm angry"));
    Receive<string>(s => s == "Angry", _ => Sender.Tell("I'm already angry!", Self));
    Receive<string>(s => s == "Happy", _ => Become(Happy));
  }
}
```
Using MoodActor:
```c#
var moodActor = system.ActorOf<MoodActor>();
moodActor.Tell("Mood?", Self);  // Result: "I'm neutral"
moodActor.Tell("Happy", Self);  // Result: becomes Happy
moodActor.Tell("Mood?", Self);  // Result: "I'm happy"
moodActor.Tell("Happy", Self);  // Result: "I'm already happy!"
moodActor.Tell("Angry", Self);  // Result: becomes Angry
moodActor.Tell("Mood?", Self);  // Result: "I'm Angry"
```

You may use lambdas if you don't want separate methods:
```c#
Receive<string>(s => s == "Grumpy", _ => Become(() =>
{
  Receive<string>(s => Sender.Tell("Leave me alone. I'm Grumpy!"));
}));
```

### Become/Unbecome
In the examples above the receive handlers are replaced when `Become()` is called. The other way of using `Become` pushes the current handler on a stack making it possible to switch back to it using `Unbecome`:
```c#
Receive<string>(s => s == "Grumpy", _ => Become(Grumpy, discardOld: false));
...
private void Grumpy()
{
  Receive<string>(s => s == "Snap out of it!", _ => Unbecome());
  Receive<string>(s => s == "Mood?", _ => Sender.Tell("I'm grumpy!"));
  Receive<string>(_ => Sender.Tell("Leave me alone. I'm Grumpy!"));
}
```
Using MoodActor:
```c#
var moodActor = system.ActorOf<MoodActor>();
moodActor.Tell("Mood?", Self);            // Result: "I'm neutral"
moodActor.Tell("Grumpy", Self);           // Result: becomes Grumpy
moodActor.Tell("Mood?", Self);            // Result: "I'm Grumpy"
moodActor.Tell("Happy", Self);            // Result: "Leave me alone. I'm Grumpy!"
moodActor.Tell("Snap out of it!", Self);  // Result: reverts back to neutral using Unbecome
moodActor.Tell("Mood?", Self);            // Result: "I'm neutral"
```

**Note**: In this case care must be taken to ensure that the number of `Unbecome()` matches the number of `Become(..., discardOld: false)` ones in the long run, otherwise this amounts to a memory leak (which is why this behavior is not the default).

#### Tip!
You can reuse Receive-specifications:
```c#
public class MoodActor : ReceiveActor
{
  public MoodActor()
  {
    Receive<string>(s => s == "Mood?", _ => Sender.Tell("I'm neutral"));
    ReceiveMoodSwitchers();
  }

  private void Happy()
  {
    Receive<string>(s => s == "Mood?", _ => Sender.Tell("I'm happy"));
    Receive<string>(s => s == "Happy", _ => Sender.Tell("I'm already happy!", Self));
    ReceiveMoodSwitchers();
  }

  private void Angry()
  {
    Receive<string>(s => s == "Mood?", _ => Sender.Tell("I'm angry")); 
    Receive<string>(s => s == "Angry", _ => Sender.Tell("I'm already angry!", Self));
    ReceiveMoodSwitchers();
  }

  private void Grumpy()
  {
    Receive<string>(s => s == "Snap out of it!", s => Unbecome());
    Receive<string>(s => Sender.Tell("Leave me alone. I'm Grumpy!"));
  }

  private void ReceiveMoodSwitchers()
  {
    Receive<string>(s => s == "Happy", _ => Become(Happy));
    Receive<string>(s => s == "Angry", _ => Become(Angry));
    Receive<string>(s => s == "Grumpy", _ => Become(Grumpy, discardOld: false));
  }
}
```

#### Warning!
Do not add other statements than Receive in Become-declarations. The result of doing so is undefined.
```c#
Receive<string>(s => s == "Grumpy", _ => Become(Grumpy, discardOld: false));
...
private void Grumpy()
{
  _state = State.Grumpy;                      //DO NOT do this
  Sender.Tell("I just became grumpy", Self);  //DO NOT do this
  Receive<string>(s => s == "Snap out of it!", s => Unbecome());
  Receive<string>(s => Sender.Tell("Leave me alone. I'm Grumpy!"));
}
```
Any state changes or message sends should be in the handler:
```c#
Receive<string>(s => s == "Grumpy", _ => 
  {
    _state = State.Grumpy;
    Sender.Tell("I just became grumpy", Self);
    Become(Grumpy, discardOld: false));
  });
...
private void Grumpy()
{
  Receive<string>(s => s == "Snap out of it!", s => Unbecome());
  Receive<string>(s => Sender.Tell("Leave me alone. I'm Grumpy!"));
}
```

###ActorBase vs UntypedActor vs ReceiveActor
TODO