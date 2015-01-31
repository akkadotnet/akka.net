---
layout: wiki
title: Contributor guidelines
---
# Contributor guidelines

## To be considered while porting Akka to Akka.NET

Here are some guidelines to keep in mind when you're considering making some changes to AkkaDotNet:

- Be .NET idiomatic, e.g. do not port `Duration` or `Future`, use `Timespan` and `Task<T>`
- Stay as close as possible to the original JVM implementation, https://github.com/akka/akka
- Do not add features that does not exist in JVM Akka into the core Akka.NET framework
- Please include relevant unit tests / specs along with your changes if appropriate.
- Try to include descriptive commit messages, even if you squash them before sending a pull request.
- If you aren't sure how something works or want to solicit input from other AkkaDotNet developers before making a change, you can [create an issue](https://github.com/akkadotnet/akka.net/issues/new) with the `discussion` tag or reach out to [AkkaDotNet on Twitter](https://twitter.com/AkkaDotNet).

## Coding conventions
- Use the default Resharper guidelines for code
  - Private member fields start with `_`, i.e. `_camelCased`
  - PascalCased public and protected Properties and Methods.
  - TODO.. Anyone got a complete list for this?
- 4 spaces for indentation
- No protected fields. Create a private field and a protected property instead.

## Tests

- Name your tests using `DisplayName=`

e.g.

```csharp
[Fact(DisplayName=
@"If a parent receives a Terminated event for a child actor, 
the parent should no longer supervise it")]
public void ClearChildUponTerminated()
{
```