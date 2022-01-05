---
uid: documentation-guidelines
title: Documentation Contribution Guidelines
---
# Documentation Contribution Guidelines

Contributions don't need to be limited just to source code - contributions to documentation are also extremely helpful and assist users in understanding the Akka.NET project.

## Website

This project uses [DocFX](https://dotnet.github.io/docfx/) to generate our website. This tool uses its own version of the [Markdown](http://daringfireball.net/projects/markdown/syntax) language named [DocFX Flavored Markdown](https://dotnet.github.io/docfx/spec/docfx_flavored_markdown.html) for crafting the documents for the website. Any editor with a valid Markdown plugin based will give you the best preview/edit experience, such as [Atom](https://atom.io/) or [StackEdit](https://stackedit.io/).

To contribute to the website's documentation, fork the main GitHub repository [Akka.NET](https://github.com/akkadotnet/akka.net). The documentation is under the [docs](https://github.com/akkadotnet/akka.net/tree/dev/docs)  directory. Please be sure to read the [`CONTRIBUTING.md`](https://github.com/akkadotnet/akka.net/blob/dev/CONTRIBUTING.md) before getting started to get acquainted with the project's workflow.

### Organization of Documentation

In order to keep the documentation discoverable for users who are unfamiliar with the Akka.NET project, we have to enforce a degree of top-down organization to achieve this.

Our general sitemap looks like this:

![Akka.NET Documentation sitemap](/images/community/contribution-standards/akkadotnet-2022-sitemap.png)

If you want to contribute a new page or documentation area, this should help you generally figure out where to categorize it. If you aren't sure where you should add a new piece of documentation, ask in [project chat](https://gitter.im/akkadotnet/akka.net) or [Akka.NET GitHub Discussions](https://github.com/akkadotnet/akka.net/discussions).

#### Moving Documentation Pages

One thing we absolutely don't tolerate is breaking existing links in our documentation as lots of external resources depend upon it. Thus, there's a procedure for moving a page from one directory to another that helps us preserve prior links.

**Step 1 - Remove the old page from `toc.yml`**.
We need to do this in order to prevent the old page from showing up in the navigation under its previous location - we'll add the new destination page back to the `toc.yml` of the appropriate directory.

**Step 2 - Move the `{filename}.md` file to its new location**.
Move the content to where it's going to live going forward.

**Step 3 - Add the moved `{filename}.md` file to the `toc.yml` of the new folder location**.
This will update the internal navigation and search to discover the new document.

**Step 4 - Add a `{filename}.html` in the old location of the previous `{filename}.md` file**.
This file is going to contain content that looks like this:

[!code[Building Akka.NET old documentation location](../building-akka-net.html)]

The HTML file uses a `meta http-equiv = "refresh"` tag to send the user, via an HTTP 301 redirect, to the new file location where the content has been moved. Yes, this is a pain but this is done in order to make sure that third party content and search engines can still find what they're looking for even after the content has been moved.

> [!NOTE]
> In the future this won't be necessary. Once DocFx3 ships native support for folder and file-level redirects will be supported: [https://github.com/dotnet/docfx/issues/3686](https://github.com/dotnet/docfx/issues/3686)

### DocFx Hygiene

This section of the documentation explains the DocFx hygiene the Akka.NET project employs in order to ensure that:

1. It's easy to correctly link between documents;
2. To reference code samples directly from the source code of the project, so those code samples are updated automatically when they're modified in-source; and
3. To make it easier to extend the documentation over a long period of time.

#### Code Samples Must Use `!code` References

One of the biggest sources of byte rot, when it comes to documentation, is that the samples embedded in it are gradually deprecated within the code it documents and are subsequently never updated. As a result of this end-users end up newly adopted already-obsolete practices, anti-patterns, and have a bad experience trying to adopt Akka.NET or any other software library.

Thus [DocFx Flavored Markdown](https://dotnet.github.io/docfx/spec/docfx_flavored_markdown.html) has a great solution for us: [`!code` snippets](https://dotnet.github.io/docfx/spec/docfx_flavored_markdown.html#code-snippet).

```markdown
[!code-<language>[<name>](<codepath><queryoption><queryoptionvalue> "<title>")]
```

These allow us to embed code directly from Akka.NET's own source code, tests, and example projects into documentation articles. This is extremely useful as it helps us ensure that when the underlying code sample gets updated the documentation articles that reference that code are subsequently updated as well.

##### Targeting Referenced Code With `#region`

So what does a real-world example of this look like? From the [`Akka.Cluster.Tools.ClusterClient` documentation](xref:cluster-client):

```markdown
[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/ClusterClient/ClientListener.cs?name=ClusterClient)]
```

This references the following code in the `Akka.Docs.Tests` project:

```csharp
#region ClusterClient
public class ClientListener : UntypedActor
{
    private readonly IActorRef _targetClient;

    public ClientListener(IActorRef targetClient)
    {
        _targetClient = targetClient;
    }

    protected override void OnReceive(object message)
    {
        Context.Become(ReceiveWithContactPoints(ImmutableHashSet<ActorPath>.Empty));
    }

    protected override void PreStart()
    {
        _targetClient.Tell(SubscribeContactPoints.Instance);
    }

    public UntypedReceive ReceiveWithContactPoints(IImmutableSet<ActorPath> contactPoints)
    {
        return (message) =>
        {
            switch (message)
            {
                // Now do something with the up-to-date "cps"
                case ContactPoints cp:
                    Context.Become(ReceiveWithContactPoints(cp.ContactPointsList));
                    break;
                // Now do something with an up-to-date "contactPoints + cp"
                case ContactPointAdded cpa:
                    Context.Become(ReceiveWithContactPoints(contactPoints.Add(cpa.ContactPoint)));
                    break;
                // Now do something with an up-to-date "contactPoints - cp"
                case ContactPointRemoved cpr:
                    Context.Become(ReceiveWithContactPoints(contactPoints.Remove(cpr.ContactPoint)));
                    break;
            }
        };
    }
}
#endregion
```

In this case we're telling DocFx to include all of the code between the `#region` and `#endregion` tags for a region named `ClusterClient` within the `ClientListener.cs` file in this directory.

This is preferable to referencing entire files or using specific line numbers because it's concise and can still be refactored in the future without having to update the documentation.

##### Targeting Referenced Code With `//<{name}>`

If you don't want to use `#region`s to target referenced code inside Akka.NET's documentation, we can also use DocFx's tag syntax to accomplish the same objective:

```markdown
[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/ClusterClient/ClientListener.cs?name=ClusterClient)]
```

Would also work if we used [DocFx's "tag" syntax](https://dotnet.github.io/docfx/spec/docfx_flavored_markdown.html#tag-name-representation-in-code-snippet-source-file):

```csharp
// <ClusterClient>
public class ClientListener : UntypedActor
{
    private readonly IActorRef _targetClient;

    public ClientListener(IActorRef targetClient)
    {
        _targetClient = targetClient;
    }

    protected override void OnReceive(object message)
    {
        Context.Become(ReceiveWithContactPoints(ImmutableHashSet<ActorPath>.Empty));
    }

    protected override void PreStart()
    {
        _targetClient.Tell(SubscribeContactPoints.Instance);
    }

    public UntypedReceive ReceiveWithContactPoints(IImmutableSet<ActorPath> contactPoints)
    {
        return (message) =>
        {
            switch (message)
            {
                // Now do something with the up-to-date "cps"
                case ContactPoints cp:
                    Context.Become(ReceiveWithContactPoints(cp.ContactPointsList));
                    break;
                // Now do something with an up-to-date "contactPoints + cp"
                case ContactPointAdded cpa:
                    Context.Become(ReceiveWithContactPoints(contactPoints.Add(cpa.ContactPoint)));
                    break;
                // Now do something with an up-to-date "contactPoints - cp"
                case ContactPointRemoved cpr:
                    Context.Become(ReceiveWithContactPoints(contactPoints.Remove(cpr.ContactPoint)));
                    break;
            }
        };
    }
}
// </ClusterClient>
```

These `// <ClusterClient>` and `// </ClusterClient>` tags would accomplish the exact same result as using `#region` and `#endregion`.

##### Finding Appropriate Code Samples

You are free to reference samples from any part of the Akka.NET code inside the documentation, but it's often best to have a dedicated code sample for each concept we want demonstrated in the documentation.

In that case it might be best to do one of the following:

1. Contribute a new code sample to the `Akka.Docs.Tests` project - these are all unit tests that are referenced somewhere in our DocFx documentation but those tests must still pass or fail or
2. Add a dedicated code sample to the `src/samples` directory, in the event that it's sufficiently complex.

#### All Pages Must Have a `uid` Defined

If you look closely at the header of each DocFx article in this repository you'll notice the following at the top of each page:

```yml
---
uid: documentation-guidelines
title: Documentation Contribution Guidelines
---
```

The `title` defines the page's `<title>` tag, but the `uid` is the canonical unique identity of a given DocFx document. It allows us to build linking systems within DocFx that don't rely on fixed directory structures.

#### All Links Between Documents Must Use `xref`

So per the previous point above about all pages needing to have their own `uid`s defined, all links between documents within the Akka.NET documentation should all be done using the `xref` format:

```markdown
[`Akka.Cluster.Tools.ClusterClient` documentation](xref:cluster-client)
```

This allows us to link to a document regardless of where it is in our file structure, which means that in the event that documentation content is refactored or re-organized the links will still work.

### Building Documentation Locally

Akka.NET's DocFx documentation can be built locally via a clone of the main [Akka.NET GitHub repository](https://github.com/akkadotnet/akka.net)

To preview the documentation for this project, execute the following commands at the root of your local clone of the repository:

#### Windows

```console
build.cmd docfx
```

#### Linux / OS X

```console
build.sh docfx
```

This will generate all of the static HTML / CSS / JS files needed to render the website into the `~/docs/_site` folder in your local repository.

In order for all of the JavaScript components to work correctly in your browser, you'll need to serve the documents via a local webserver rather than the file system. You can launch DocFx's build in server via the following script in the root of this repository:

```console
serve-docs.cmd
```

This will use the built-in `docfx.console` binary that is installed as part of the NuGet restore process from executing any of the usual `build.cmd` or `build.sh` steps to preview the fully-rendered documentation.

### Markdown Linting

[Akka.NET's build system](xref:building-and-distributing) leverages [`markdownlint`](https://github.com/DavidAnson/markdownlint) (via [`markdown-cli`](https://github.com/igorshubovych/markdownlint-cli)) to validate formatting of the articles, headline capitalization, and lots of other details.

To run `markdownlint` locally you'll want to have [Node.JS](https://nodejs.org/en/) installed along with Node Package Manager (`npm`).

**Installation**
To install `markdownlint-cli` execute this command to add it globally to your `npm` command line:

    npm install -g markdownlint-cli markdownlint-rule-titlecase

**Run**
To run the markdown linting rules for Akka.NET's documentation, in the root directory of the Akka.NET GitHub repository:

    markdownlint "docs/**/*.md" --rules "markdownlint-rule-titlecase"

If there are any linting errors the exact filename, line number, and rule infraction will be listed there. This is the exact same command we run in Akka.NET's pull request validation system.

## Code

When documenting code, please use the standard .NET convention of [XML triple-slash documentation comments](https://msdn.microsoft.com/en-us/library/vstudio/b2s063f7). This allows the project to use tools like Sandcastle to generate the API documentation for the project. The latest stable API documentation can be found [here](https://getakka.net/api/index.html).

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
