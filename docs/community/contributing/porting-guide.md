---
uid: porting-guide
title: Scala To C# Conversion Guide
---
# Scala To C# Conversion Guide

* Be .NET idiomatic, e.g. do not port `Duration` instead of `TimeSpan` and `Future` instead of `Task<T>`
* Stay as close as possible to the original JVM implementation,
* Do not add features that don't exist in JVM Akka into the core Akka.NET

## Case Classes

From

```scala
final case class HandingOverData(singleton: ActorRef, name: String)
```

All parameters of the case class should become a public property

Simple implementation

```c#
public sealed class HandingOverData
{
    public HandingOverData(IActorRef singleton, string name)
    {
        Singleton = singleton;
        Name = name;
    }

    public IActorRef Singleton { get; }

    public string Name { get; }
}
```

Complex implementation

```c#
public sealed class HandingOverData
{
    public HandingOverData(IActorRef singleton, string name)
    {
        Singleton = singleton;
        Name = name;
    }

    public IActorRef Singleton { get; }

    public string Name { get; }

    private bool Equals(HandingOverData other)
    {
        return Equals(Singleton, other.Singleton) && string.Equals(Name, other.Name);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        return obj is HandingOverData && Equals((HandingOverData)obj);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((Singleton?.GetHashCode() ?? 0) * 397) ^ (Name?.GetHashCode() ?? 0);
        }
    }

    public override string ToString() => $"{nameof(HandingOverData)}<{nameof(Singleton)}: {Singleton}, {nameof(Name)}: {Name}>";
}
```

In order to support C# 7 deconstruction you could add `Deconstruct` method

```c#
public void Deconstruct(out IActorRef singleton, out string name)
{
    singleton = Singleton;
    name = Name;
}
```

Some messages should implement `With` (from C# 8 records spec) or `Copy` method.

```c#
public HandingOverData With(IActorRef singleton = null, string name = null) 
    => new HandingOverData(singleton ?? Singleton, name ?? Name);
```

In some cases it would be a good idea (mandatory for value types) to implement `IEquatable<T>`

```c#
public sealed class HandingOverData : IEquatable<HandingOverData>
{
    ...
    public bool Equals(HandingOverData other)
    {
        return Equals(Singleton, other.Singleton) && string.Equals(Name, other.Name);
    }
    ...
}
```

## Case Object

From

```scala
case object RecoveryCompleted
```

Simple implementation

```c#
public sealed class RecoveryCompleted
{
    public static RecoveryCompleted Instance { get; } = new RecoveryCompleted();
    private RecoveryCompleted() {}
}
```

Complex implementation

```c#
public sealed class RecoveryCompleted
{
    public static RecoveryCompleted Instance { get; } = new RecoveryCompleted();

    private RecoveryCompleted() {}
    public override bool Equals(object obj) => !ReferenceEquals(obj, null) && obj is RecoveryCompleted;
    public override int GetHashCode() => nameof(RecoveryCompleted).GetHashCode();
    public override string ToString() => nameof(RecoveryCompleted);
}
```

In some cases it would be a good idea (mandatory for value types) to implement `IEquatable<T>`

```c#
public sealed class RecoveryCompleted : IEquatable<RecoveryCompleted>
{
    ...
    public bool Equals(RecoveryCompleted other) => true;
    ...
}
```

## Class Constructors

From

```scala
class Person(name: String, var surname: string, val age: Int, private val position: String)
{
}
```

To

```C#
public class Person
{
    private readonly string _name;
    private readonly string _position;
    
    public Person(string name, string surname, int age, string position)
    {
        _name = name;
        Surname = surname;
        Age = age;
        _position = position;
    }
    
    public string Surname { get; set; }
    public int Age { get; }
}
```

## LINQ (Collection's Methods)

| scala                                              | C#                                       |
|---                                                 |---                                       |
| collectFirst(func)                                 | FirstOrDefault(func)                     |
| drop(count)                                        | Skip(count)                              |
| dropRight(count)                                   | Reverse().Skip(count).Reverse()          |
| exists(func)                                       | Any(func)                                |
| filter(func)                                       | Where(func)                              |
| filterNot(func)                                    | `<none>`                                 |
| flatMap(func)                                      | SelectMany(func)                         |
| flatten                                            | SelectMany(i => i)                       |
| fold(initval)(func)                                | `<none>`                                 |
| foldLeft(initval)(func)                            | Aggregate(initval, func)                 |
| foldRight(initval)(func)                           | Reverse().Aggregate(initval, func)       |
| forall(func)                                       | All(func)                                |
| foreach(func)                                      | `<none>`                                 |
| head                                               | First()                                  |
| headOption                                         | FirstOrDefault()                         |
| last                                               | Last()                                   |
| lastOption                                         | LastOrDefault()                          |
| map(func)                                          | Select(func)                             |
| mkString(separator)                                | string.Join(separator, array)            |
| reduce(func)                                       | `<none>`                                 |
| reduceLeft(func)                                   | Aggregate(func)                          |
| reduceRight(func)                                  | Reverse().Aggregate(func)                |
| sorted(implicit ordering)                          | OrderBy(i -> i, comparer)                |
| tail                                               | Skip(1)                                  |
| take(count)                                        | Take(count)                              |
| takeRight(count)                                   | Reverse().Take(count).Reverse()          |
| zip(seq2, func)                                    | Zip(seq2, func)                          |
| zipWithIndex                                       | `<none>`                                 |

## Currying

From

```scala
def bufferOr(grouping: String, message: Any, originalSender: ActorRef)(action: ⇒ Unit): Unit = {
    buffers.get(grouping) match {
      case None ⇒ action
      case Some(messages) ⇒
        buffers = buffers.updated(grouping, messages :+ ((message, originalSender)))
        totalBufferSize += 1
    }
}
```

To

```C#
public void BufferOr(string grouping, object message, IActorRef originalSender, Action action)
{
    BufferedMessages messages = null;
    if (_buffers.TryGetValue(grouping, out messages))
    {
        _buffers[grouping].Add(new KeyValuePair<object, IActorRef>(message, originalSender));
        _totalBufferSize += 1;
    }
    else {
        action();
    }  
}
```

## Exceptions

| scala                     | C#                          |
|---                        |---                          |
| IllegalArgumentException  | ArgumentException           |
| IllegalStateException     | InvalidOperationException   |
| ArithmeticException       | ArithmeticException         |
| NullPointerException      | NullReferenceException      |
| NotSerializableException  | SerializationException      |

## Pattern Matching

### Constant Patterns

```scala
def testPattern(x: Any): String = x match {
   case 0 => "zero"
   case true => "true"
   case "hello" => "you said 'hello'"
   case Nil => "an empty List"
}
```

C# 7 supports all constant patterns

```C#
public string TestPattern(object x)
{
    switch(x)
    {
        case 0: return "zero";
        case true: return "true";
        case "hello": return "you said Hello";
        case null: return "an empty list";                
    }
    return string.Empty;
}
```

C# 8 supports it even closer

```c#
public string TestPattern(object x) 
{
    return x switch 
    {
        0 => "zero",
        true => "true",
        "hello" => "you said Hello",
        null => "an empty list",
        _ => string.Empty
    };
}
```

### Sequence Patterns

```scala
def testPattern(x: Any): String = x match {
   case List(0, _, _) => "a three-element list with 0 as the first element"
   case List(1, _*) => "a list beginning with 1, having any number of elements"
   case Vector(1, _*) => "a vector starting with 1, having any number of elements"
}
```

C# does not support sequence patterns

### Tuples Patterns

```scala
def testPattern(x: Any): String = x match {
   case (a, b) => s"got $a and $b"
   case (a, b, c) => s"got $a, $b, and $c"
}
```

C# 8 supports tuple patterns by using type patterns

```c#
public static string TestPattern(object x) => x switch
{
    ValueTuple<object, object>(var a, var b) => $"got {a} and {b}",
    ValueTuple<object, object, object>(var a, var b, var c) => $"got {a}, {b}, and {c}",
    _ => string.Empty
};
```

C# 9 got it even closer

```c#
public static string TestPattern(object x) => x switch 
{
    (object a, object b) => $"got {a} and {b}",
    (object a, object b, object c) => $"got {a}, {b}, and {c}",
    _ => string.Empty
};
```

### Constructor Patterns (Case Classes)

```scala
def testPattern(x: Any): String = x match {
   case Person(first, "Alexander") => s"found an Alexander, first name = $first"
   case Dog("Suka") => "found a dog named Suka"
}
```

C# 7 does not support constructor patterns, but you could use an equivalent using typed pattern

```C#
public string TestPattern(object x)
{
    switch(x)
    {
        case Person p when p.LastName == "Alexander": return $"found an Alexander, first name = {p.FirstName}";
        case Dog d when d.Name == "Suka": return "found a dog named Suka";  
    }
    return string.Empty;
}
```

C# 8 supports the property pattern

```c#
public static string TestPattern(object x) => x switch
{
    Person { FirstName: "Alexander" } p =>  $"found an Alexander, first name = {p.FirstName}",
    Dog { Name: "Suka" } d => "found a dog named Suka",
    _ => string.Empty
};
```

### Typed Patterns

```scala
def testPattern(x: Any): String = x match {
   case s: String => s"you gave me this string: $s"
   case i: Int => s"thanks for the int: $i"
   case f: Float => s"thanks for the float: $f"
   case a: Array[Int] => s"an array of int: ${a.mkString(",")}"
   case as: Array[String] => s"an array of strings: ${as.mkString(",")}"
   case d: Dog => s"dog: ${d.name}"
   case list: List[_] => s"thanks for the List: $list"
   case m: Map[_, _] => m.toString
}
```

You can use typed patterns in C# 7

```C#
public string TestPattern(object x)
{
    switch(x)
    { 
        case string s: return $"you gave me this string: {s}";
        case int i: return $"thanks for the int: {i}";
        case float f: return $"thanks for the float: {f}";
        case int[] a: return $"an array of int: {string.Join(",", a)}";
        case Dog d: return $"dog: ${d.Name}";
        case List<int> list: return $"thanks for the List: {list}";
        case Dictionary<int, string> dict: return $"dictionary: {dict}";
    }
    return string.Empty;
}
```

### Patterns on Option

From

```scala
def matchingRole(member: Member, role: String): Boolean = role match {
    case None    ⇒ true
    case Some(r) ⇒ member.hasRole(r)
}
```

To

```c#
public bool MatchingRole(Member member, string role)
{
    switch (member)
    {
        case Member m:
            return m.HasRole(role);
        default:
            return true;
    }
}
```

Or in C# 8

```c#
public bool MatchingRole(Member member, string role)
    => member switch 
    {
        Member m => m.HasRole(role),
        _ => true
    }
```

### Extractors

From

```scala
trait User {
  def name: String
}
class FreeUser(val name: String) extends User
class PremiumUser(val name: String) extends User

object FreeUser {
  def unapply(user: FreeUser): Option[String] = Some(user.name)
}
object PremiumUser {
  def unapply(user: PremiumUser): Option[String] = Some(user.name)
}

// using
val user: User = new PremiumUser("Daniel")
user match {
  case FreeUser(name) => "Hello " + name
  case PremiumUser(name) => "Welcome back, dear " + name
}
```

To

```C#
public interface IUser
{
    string Name { get; }
}

public class FreeUser : IUser
{
    public FreeUser(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

public class PremiumUser : IUser
{
    public PremiumUser(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

public static class UserExtensions
{
    public static bool TryExtractName(this IUser user, out string name)
    {
        name = user.Name;
        return !string.IsNullOrEmpty(user.Name);
    }
}

// using
IUser user = new PremiumUser("Daniel");
```

In C# 7

```c#
string message = string.Empty;
switch (user)
{
    case FreeUser p when p.TryExtractName(out var name):
        message = $"Hello {name}";
        break;
    case PremiumUser p when p.TryExtractName(out var name):
        message = $"Welcome back, dear {name}"
        break;
}
```

In C# 8

```c#
var message = user switch
{
    FreeUser p when p.TryExtractName(out var name) => $"Hello {name}",
    PremiumUser p when p.TryExtractName(out var name) => $"Welcome back, dear {name}",
    _ => string.Empty
};
```

## Require

```scala
require(cost > 0, "cost must be > 0")
```

use `ArgumentException`

```C#
if (cost <= 0) throw ArgumentException("cost must be > 0", nameof(cost));
```

## Not Covered Topics

* Traits
* Partial functions
* Local functions/Nested Methods
* Tail call recursion
* For Comprehensions
* Default Parameter Values
* Implicit parameters

# Tests

* Prefer to use `FluentAssertions` in tests, instead of `Xunit assertions` and `AkkaSpecExtensions`

## Intercept[T]

Akka uses intecept to check that an exception was thrown

```scala
intercept[IllegalArgumentException] {
  val serializer = new MiscMessageSerializer(system.asInstanceOf[ExtendedActorSystem])
  serializer.manifest("INVALID")
}
```

in C# you have 2 options

```c#
var serializer = new MiscMessageSerializer(Sys.AsInstanceOf<ExtendedActorSystem>());

// use FluentAssertions
Action comparison = () => serializer.Manifest("INVALID");
comparison.ShouldThrow<ArgumentException>();

// use Xunit2 asserts
Assert.Throws<ArgumentException>(() => serializer.Manifest("INVALID"));
```
