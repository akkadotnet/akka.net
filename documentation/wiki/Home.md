# Documentation

Wiki guidelines:

Start with a H1 header 
```
# My header
```
Headers are used when generating the ToC for the Wiki site.

Code is documented using backtick fence notation:

    ```csharp
    public void foo()
    {
    }
    ```

HOCON configurations are declared as:

    ```hocon
    akka.actor {
       #some config
    }
    ```

Github does not have HOCON support, but on the Akka.NET site, we do have a syntax highlighter for it. 

Notifications are done using backquote notation followed by `**warning**` or `**notification**`

    >**note** some notification

To auto link to classes, methods, or Akka constructs use back tick `ActorSystem`.
This makes the Akka.NET site link to that page if there is one.



* [Documentation](Documentation)

Use the menu on the right to navigate to different pages.
