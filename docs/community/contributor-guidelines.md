---
uid: contributor-guidelines
title: Contributor guidelines
---
# Contributor guidelines

## To be considered while porting Akka to Akka.NET

Here are some guidelines to keep in mind when you're considering making some
changes to Akka.NET:

- Be .NET idiomatic, e.g. do not port `Duration` or `Future`, use `Timespan`
  and `Task<T>`.
- Stay as close as possible to the original JVM implementation,
  https://github.com/akka/akka.
- Do not add features that do not exist in JVM Akka into the core Akka.NET
  framework.
- Please include relevant unit tests / specs along with your changes, if appropriate.
- Try to include descriptive commit messages, even if you squash them before
  sending a pull request.
- If you aren't sure how something works or want to solicit input from other
  Akka.NET developers before making a change, you can [create an issue](https://github.com/akkadotnet/akka.net/issues/new)
  with the `discussion` tag or reach out to [AkkaDotNet on Twitter](https://twitter.com/AkkaDotNet).

## Coding conventions
- Use the default Resharper guidelines for code
  - Start private member fields with `_`, i.e. `_camelCased`
  - Use PascalCase for public and protected Properties and Methods
  - Avoid using `this` when accessing class variables, e.g. BAD `this.fieldName`
  - TODO.. Anyone got a complete list for this?
- 4 spaces for indentation
- Use [Allman style](http://en.wikipedia.org/wiki/Indent_style#Allman_style)
  brackets for C# code and [1TBS style](http://en.wikipedia.org/wiki/Indent_style#Variant:_1TBS)
  brackets for HOCON and F# code.
- Do not use protected fields - create a private field and a protected property instead

## Tests

- Name your tests using `DisplayName=`

e.g.

```csharp
[Fact(DisplayName=
@"If a parent receives a Terminated event for a child actor,
the parent should no longer supervise it")]
public void ClearChildUponTerminated()
{
  ...
}
```
