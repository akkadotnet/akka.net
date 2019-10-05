---
uid: documentation-guidelines
title: Documentation guidelines
---
# Documentation guidelines

When developers or users have problems with software the usual forum quip is to read the manual. Sometimes in nice tones and others not so nice. It's great when the documentation is succinct and easy to read and comprehend. All too often, though, there are huge swathes of missing, incomplete, or downright wrong bits that leave people more confused than they were before they read the documentation.

So the call goes out for people to help build up the documentation. Which is great until you have a lot of people with their own ideas how everything should be laid out trying to contribute. To alleviate the confusion, guidelines are setup. This document illustrates the documentation guidelines for this project.

There is a ton of work that still needs to be done, especially in the API documentation department. Please don't hesitate to join and contribute to the project. We welcome everyone and could use your help.

## Website

This project uses [DocFX](https://dotnet.github.io/docfx/) to generate our website. This tool uses its own version of the [Markdown](http://daringfireball.net/projects/markdown/syntax) language named 
[DocFX Flavored Markdown](https://dotnet.github.io/docfx/spec/docfx_flavored_markdown.html) for crafting the documents for the website. Any editor with a valid Markdown plugin based will give you the best preview/edit experience, such as [Atom](https://atom.io/) or [StackEdit](https://stackedit.io/).

To contribute to the website's documentation, fork the main github repository [Akka.Net](https://github.com/akkadotnet/akka.net). The documentation is under the [docs](https://github.com/akkadotnet/akka.net/tree/dev/docs)  directory. Please be sure to read the [Contributing.md](https://github.com/akkadotnet/akka.net/blob/dev/CONTRIBUTING.md) before getting started to get acquainted with the project's workflow.

## Code

When documenting code, please use the standard .NET convention of [XML documentation comments](https://msdn.microsoft.com/en-us/library/vstudio/b2s063f7). This allows the project to use tools like Sandcastle to generate the API documentation for the project. The latest stable API documentation can be found [here](https://getakka.net/api/index.html).

Please be mindful to including *useful* comments when documenting a class or method. *Useful* comments means including full English sentences when summarizing the code and not relying on pre-generated comments from a tool like GhostDoc. Tools like these are great in what they do *if* supplemented with well-reasoned grammar.

**BAD** obviously auto-generated comment
```csharp
/// <summary>
/// Class Serializer.
/// </summary>
public abstract class Serializer
{
    /// <summary>
    ///     Froms the binary.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="type">The type.</param>
    /// <returns>System.Object.</returns>
    public abstract object FromBinary(byte[] bytes, Type type);
}
```

**GOOD** clear succinct comment
```csharp
/// <summary>
/// A Serializer represents a bimap between an object and an array of bytes representing that object.
/// </summary>
public abstract class Serializer
{
    /// <summary>
    /// Deserializes a byte array into an object of type <paramref name="type"/>
    /// </summary>
    /// <param name="bytes">The array containing the serialized object</param>
    /// <param name="type">The type of object contained in the array</param>
    /// <returns>The object contained in the array</returns>
    public abstract object FromBinary(byte[] bytes, Type type);
}
```

We've all seen the bad examples at one time or another, but rarely do we see the good examples. A nice rule of thumb is to write the comments you would want to read while perusing the API documentation.
