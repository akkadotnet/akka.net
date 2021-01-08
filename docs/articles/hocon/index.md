---
uid: hocon
title: HOCON Syntax and Practices in Akka.NET
---

# HOCON (Human-Optimized Config Object Notation)

> [!WARNING]
> This documentation pertains to the HOCON 3.0 specification, which is currently not in-use inside Akka.NET. [Subscribe to updates here](https://github.com/akkadotnet/akka.net) by clicking "Watch" on the repository.

This is an abridged version of HOCON for its use in Akka.NET. A full .NET implementation of HOCON spec can be read [here](https://github.com/akkadotnet/HOCON/blob/dev/README.md)

You can play around with HOCON syntax in real-time by going to [hocon-playground](https://hocon-playground.herokuapp.com/)

## Definitions

 - A **field** is a key-value pair consisting a _key_, _separator_, and a _value_.
 - A **separator** is the `:` or `=` character.
 - A **key** is a string to the left of the _separator_.
 - A **value** is any "value" to the right of the _separator_, as defined in the JSON spec, plus unquoted strings, triple quoted strings and substitutions.
 - A **simple value** or a **literal value** is any value that are not objects or arrays.
 - A **newline** is defined as the newline character `\n` (unicode value `0x000A`)
 - References to a **file** ("the file being parsed") can be understood to mean any byte stream being parsed, not just literal files in a filesystem.

## Syntax

Much of this is defined with reference to JSON; you can find the JSON spec at http://json.org/.

### Unchanged from JSON

 - files must be valid UTF-8
 - quoted strings are in the same format as JSON strings
 - values have possible types: string, number, object, array, boolean, null
 - allowed number formats matches JSON.

### Comments

Anything between `//` or `#` and the next newline is considered a comment and ignored, unless the `//` or `#` is inside a quoted string.

### Omit root braces

JSON documents must have an array or object at the root. Empty
files are invalid documents, as are files containing only a
non-array non-object value such as a string.

In HOCON, if the file does not begin with a square bracket or
curly brace, it is parsed as if it were enclosed with `{}` curly
braces.

A HOCON file is invalid if it omits the opening `{` but still has
a closing `}`; the curly braces must be balanced.

### Key-value separator

You can use the `=` character instead of the standard JSON `:` separator.

If a key is followed by `{`, the `:` or `=` may be omitted. So `"foo" {}` means `"foo" : {}`

### Commas

Values in arrays, and fields in objects, need not have a comma between them as long as they have at least one ASCII newline (`\n`, unicode value `0x000A`) between them.

The last element in an array or last field in an object may be followed by a single comma. This extra comma is ignored.

These same comma rules apply to fields in objects.

Examples:
 - `[1,2,3,]` and `[1,2,3]` are the same array.
 - `[1\n2\n3]` and `[1,2,3]` are the same array.
 - `[1,2,3,,]` is invalid because it has two trailing commas.
 - `[,1,2,3]` is invalid because it has an initial comma.
 - `[1,,2,3]` is invalid because it has two commas in a row.

### Duplicate keys and object merging

Duplicate keys that are declared later in the string have different behaviours:

 - A key with any values will override previous values if they are of different types.
 - A key with literal or array value will override any previous key value.
 - A key with object value will be recursively merged with previous object value.

Objects are merged by:

 - Fields present in only one of the two objects are added to the merged object.
 - Non-object fields in overriding object will override field with the same path on previous object.
 - Object fields with the same path in both objects will be recursively merged according to these same rules.


    {
        "foo" : { "a" : 42 },
        "foo" : { "b" : 43 }
    }

will be merged to:

    {
        "foo" : { "a" : 42, "b" : 43 }
    }


In this example:

    {
        "foo" : { "a" : 42 },
        "foo" : null,
        "foo" : { "b" : 43 }
    }

the declaration of `"foo" : null` blocks the merge, so that the final result is:

    {
        "foo" : { "b" : 43 }
    }

#### Array and object concatenation

Arrays can be concatenated with arrays, and objects with objects, but it is an error if they are mixed.

For purposes of concatenation, "array" also means "substitution that resolves to an array" and "object" also means "substitution that resolves to an object."

Within an field value or array element, if only non-newline whitespace separates the end of a first array or object or substitution from the start of a second array or object or substitution, the two values are concatenated. Newlines may occur _within_ the array or object, but not _between_ them. Newlines _between_ prevent concatenation.

For objects, "concatenation" means "merging", so the second object overrides the first.

Arrays and objects cannot be field keys, whether concatenation is involved or not.

Here are several ways to define `a` to the same object value:

    // one object
    a : { b : 1, c : 2 }
    // two objects that are merged via concatenation rules
    a : { b : 1 } { c : 2 }
    // two fields that are merged
    a : { b : 1 }
    a : { c : 2 }

Here are several ways to define `a` to the same array value:

    // one array
    a : [ 1, 2, 3, 4 ]
    // two arrays that are concatenated
    a : [ 1, 2 ] [ 3, 4 ]
    // a later definition referring to an earlier
    // (see "self-referential substitutions" below)
    a : [ 1, 2 ]
    a : ${a} [ 3, 4 ]

A common use of object concatenation is "inheritance":

    data-center-generic = { cluster-size = 6 }
    data-center-east = ${data-center-generic} { name = "east" }

A common use of array concatenation is to add to paths:

    path = [ /bin ]
    path = ${path} [ /usr/bin ]

#### Note: Arrays without commas or newlines

Arrays allow you to use newlines instead of commas, but not whitespace instead of commas. Non-newline whitespace will produce concatenation rather than separate elements.

    // this is an array with one element, the string "1 2 3 4"
    [ 1 2 3 4 ]
    // this is an array of four integers
    [ 1
      2
      3
      4 ]

    // an array of one element, the array [ 1, 2, 3, 4 ]
    [ [ 1, 2 ] [ 3, 4 ] ]
    // an array of two arrays
    [ [ 1, 2 ]
      [ 3, 4 ] ]

If this gets confusing, just use commas. The concatenation behavior is useful rather than surprising in cases like:

    [ This is an unquoted string my name is ${name}, Hello ${world} ]
    [ ${a} ${b}, ${x} ${y} ]

### Path expressions

Path expressions are used to write out a path through the object graph. They appear in two places; in substitutions, like `${foo.bar}`, and as the keys in objects like `{ foo.bar : 42 }`.

Path expressions are syntactically identical to a value concatenation, except that they may not contain substitutions. This means that you can't nest substitutions inside other substitutions, and you can't have substitutions in keys.

When concatenating the path expression, any `.` characters outside quoted strings are understood as path separators, while inside quoted strings `.` has no special meaning. So `foo.bar."hello.world"` would be a path with three elements, looking up key `foo`, key `bar`, then key `hello.world`.

### Paths as keys

If a key is a path expression with multiple elements, it is expanded to create an object for each path element other than the last. The last path element, combined with the value, becomes a field in the most-nested object.

In other words:

    foo.bar : 42

is equivalent to:

    foo { bar : 42 }

and:

    foo.bar.baz : 42

is equivalent to:

    foo { bar { baz : 42 } }

and so on. These values are merged in the usual way; which implies that:

    a.x : 42,
    a.y : 43

is equivalent to:

    a { x : 42, y : 43 }

Because path expressions work like value concatenations, you can have whitespace in keys:

    a b c : 42

is equivalent to:

    "a b c" : 42

Because path expressions are always converted to strings, even single values that would normally have another type become strings.

   - `true : 42` is `"true" : 42`
   - `3 : 42` is `"3" : 42`
   - `3.14 : 42` is `"3" : { "14" : 42 }`

As a special rule, the unquoted string `include` may not begin a path expression in a key, because it has a special interpretation (see below).

### Substitutions

Substitutions are a way of referring to other parts of the configuration tree.

The syntax is `${pathexpression}` or `${?pathexpression}` where the `pathexpression` is a path expression as described above. This path expression has the same syntax that you could use for an object key.

 - When you start a substitution with `${?`, the substitution is an _optional substitution_. The `?` in `${?pathexpression}` must not have whitespace before it; the three characters `${?` must be exactly like that, grouped together.
 - A substitution without question mark (`${`) is called a _required substitution_.

Substitutions are not parsed inside quoted strings. To get a string containing a substitution, you must use value concatenation with the substitution in the unquoted portion:

    key : ${animal.favorite} is my favorite animal

Or you could quote the non-substitution portion:

    key : ${animal.favorite}" is my favorite animal"

Substitutions are resolved by looking up the path in the configuration. The path begins with the root configuration object, i.e. it is "absolute" rather than "relative."

Substitution processing is performed as the last parsing step, so a substitution can look forward in the configuration. If a configuration consists of multiple files, it may even end up retrieving a value from another file.

If a key has been specified more than once, the substitution will always evaluate to its latest-assigned value (that is, it will evaluate to the merged object, or the last non-object value that was set, in the entire document being parsed including all included files).

If a substitution does not match any value present in the configuration and is not resolved by an external source, then it is undefined. An undefined _required substitution_ is invalid and will generate an error.

[!code-csharp[ConfigurationSample](../../../src/core/Akka.Docs.Tests/Configuration/ConfigurationSample.cs?name=UnsolvableSubstitutionWillThrowSample)]

If an _optional substitution_ is undefined:

 - If it is the value of an object field then the field would not be created. If the field would have overridden a previously-set value for the same field, then the previous value remains.
 - If it is an array element then the element would not be added.
 - If it is part of a value concatenation with another string then it would become an empty string; if part of a value concatenation with an object or array it would become an empty object or array.

`foo : ${?bar}` would avoid creating field `foo` if `bar` is undefined. `foo : ${?bar}${?baz}` would also avoid creating the field if _both_ `bar` and `baz` are undefined.

Substitutions are only allowed in field values and array elements (value concatenations), they are not allowed in keys or nested inside other substitutions (path expressions).

A substitution is replaced with any value type (number, object, string, array, true, false, null). If the substitution is the only part of a value, then the type is preserved. Otherwise, it is value-concatenated to form a string.

Examples:

[!code-csharp[ConfigurationSample](../../../src/core/Akka.Docs.Tests/Configuration/ConfigurationSample.cs?name=StringSubstitutionSample)]
[!code-csharp[ConfigurationSample](../../../src/core/Akka.Docs.Tests/Configuration/ConfigurationSample.cs?name=ArraySubstitutionSample)]
[!code-csharp[ConfigurationSample](../../../src/core/Akka.Docs.Tests/Configuration/ConfigurationSample.cs?name=ObjectMergeSubstitutionSample)]

#### Using substitution to access environment variables

For substitutions which are not found in the configuration tree, it will be resolved by looking at system environment variables.

[!code-csharp[ConfigurationSample](../../../src/core/Akka.Docs.Tests/Configuration/ConfigurationSample.cs?name=EnvironmentVariableSample)]

An application can explicitly block looking up a substitution in the environment by setting a value in the configuration, with the same name as the environment variable. You could set `HOME : null` in your root object to avoid expanding `${HOME}` from the environment, for example:

[!code-csharp[ConfigurationSample](../../../src/core/Akka.Docs.Tests/Configuration/ConfigurationSample.cs?name=BlockedEnvironmentVariableSample)]

It's recommended that HOCON keys always use lowercase, because environment variables generally are capitalized. This avoids naming collisions between environment variables and configuration properties. (While on Windows `Environment.GetEnvironmentVariable()` is generally not case-sensitive, the lookup will be case sensitive all the way until the env variable fallback lookup is reached).

Environment variables are interpreted as follows:

 - Env variables set to the empty string are kept as such (set to empty string, rather than undefined)
 - If `Environment.GetEnvironmentVariable()` throws SecurityException, then it is treated as not present
 - Encoding is handled by C# (`Environment.GetEnvironmentVariable()` already returns a Unicode string)
 - Environment variables always become a string value, though if an app asks for another type automatic type conversion would kick in

##### Note on Windows and case sensitivity of environment variables

HOCON's lookup of environment variable values is always case sensitive, but Linux and Windows differ in their handling of case.

Linux allows one to define multiple environment variables with the same name but with different case; so both "PATH" and "Path" may be defined simultaneously. HOCON's access to these environment variables on Linux is straightforward; ie just make sure you define all your vars with the required case.

Windows is more confusing. Windows environment variables names may contain a mix of upper and lowercase characters, eg "Path", however Windows does not allow one to define multiple instances of the same name but differing in case.
Whilst accessing env vars in Windows is case insensitive, accessing env vars in HOCON is case sensitive.

So if you know that you HOCON needs "PATH" then you must ensure that the variable is defined as "PATH" rather than some other name such as "Path" or "path".
However, Windows does not allow us to change the case of an existing env var; we can't simply redefine the var with an upper case name.
The only way to ensure that your environment variables have the desired case is to first undefine all the env vars that you will depend on then redefine them with the required case.

For example, the the ambient environment might have this definition ...

```
set Path=A;B;C
```
... we just don't know. But if the HOCON needs "PATH", then the start script must take a precautionary approach and enforce the necessary case as follows ...

```
set OLDPATH=%PATH%
set PATH=
set PATH=%OLDPATH%
```

You cannot know what ambient environment variables might exist in the ambient environment when your program is invoked, nor what case those definitions might have. Therefore the only safe thing to do is redefine all the vars you rely on as shown above.

#### Self-Referential Substitutions

The idea of self-referential substitution is to allow a new value for a field to be based on the older value.

```
    path : "a:b:c"
    path : ${path}":d"
```
is equal to:
```
	path : "a:b:c:d"
```

Examples of self-referential fields:

[!code-csharp[ConfigurationSample](../../../src/core/Akka.Docs.Tests/Configuration/ConfigurationSample.cs?name=SelfReferencingSubstitutionWithString)]
[!code-csharp[ConfigurationSample](../../../src/core/Akka.Docs.Tests/Configuration/ConfigurationSample.cs?name=SelfReferencingSubstitutionWithArray)]

Note that an object or array with a substitution inside it is **not** considered self-referential for this purpose. The self-referential rules do **not** apply to:

 - `a : { b : ${a} }`
 - `a : [${a}]`

These cases are unbreakable cycles that generate an error.

Examples:

[!code-csharp[ConfigurationSample](../../../src/core/Akka.Docs.Tests/Configuration/ConfigurationSample.cs?name=CircularReferenceSubstitutionError)]

#### The `+=` field separator

Fields may have `+=` as a separator rather than `:` or `=`. A field with `+=` transforms into a self-referential array
concatenation, like this:

    a += b

becomes:

    a = ${?a} [b]

`+=` appends an element to a previous array. If the previous value was not an array, an error will result just as it would in the long form `a = ${?a} [b]`. Note that the previous value is optional (`${?a}` not `${a}`), which allows `a += b` to be the first mention of `a` in the file (it is not necessary to have `a = []` first).

Example:

[!code-csharp[ConfigurationSample](../../../src/core/Akka.Docs.Tests/Configuration/ConfigurationSample.cs?name=PlusEqualOperatorSample)]

#### Examples of Self-Referential Substitutions

In isolation (with no merges involved), a self-referential field is an error because the substitution cannot be resolved:

    foo : ${foo} // an error

When `foo : ${foo}` is merged with an earlier value for `foo`, however, the substitution can be resolved to that earlier value. When merging two objects, the self-reference in the overriding field refers to the overridden field. Say you have:

    foo : { a : 1 }
    foo : ${foo}

Then `${foo}` resolves to `{ a : 1 }`, the value of the overridden field.

It would be an error if these two fields were reversed, so:

    foo : ${foo}
    foo : { a : 1 }

Here the `${foo}` self-reference comes before `foo` has a value, so it is undefined, exactly as if the substitution referenced a path not found in the document.

Because `foo : ${foo}` conceptually looks to previous definitions of `foo` for a value, the optional substitution syntax `${?foo}` does not create a cycle:

    foo : ${?foo} // this field just disappears silently

If a substitution is hidden by a value that could not be merged with it (by a non-object value) then it is never evaluated and no error will be reported. So for example:

    foo : ${does-not-exist}
    foo : 42

In this case, no matter what `${does-not-exist}` resolves to, we know `foo` is `42`, so `${does-not-exist}` is never evaluated and there is no error. The same is true for cycles like `foo : ${foo}, foo : 42`, where the initial self-reference are simply ignored.

A self-reference resolves to the value "below" even if it's part of a path expression. So for example:

    foo : { a : { c : 1 } }
    foo : ${foo.a}
    foo : { a : 2 }

Here, `${foo.a}` would refer to `{ c : 1 }` rather than `2` and so the final merge would be `{ a : 2, c : 1 }`.

Recall that for a field to be self-referential, it must have a substitution or value concatenation as its value. If a field has an object or array value, for example, then it is not self-referential even if there is a reference to the field itself inside that object or array.

Substitution can refer to paths within themselves, for example:

    bar : { foo : 42,
            baz : ${bar.foo}
          }

Because there is no inherent cycle here, the substitution will "look forward" (including looking at the field currently being defined). To make this clearer, in the example below, `bar.baz` would be `43`:

    bar : { foo : 42,
            baz : ${bar.foo}
          }
    bar : { foo : 43 }

Mutually-referring objects would also work, and are not self-referential (so they look forward):

    // bar.a will end up as 4 and foo.c will end up as 3
    bar : { a : ${foo.d}, b : 1 }
    bar.b = 3
    foo : { c : ${bar.b}, d : 2 }
    foo.d = 4

One tricky case is an optional self-reference in a value concatenation, in this example `a` would be `foo` not `foofoo` because the self reference has to "look back" to an undefined `a`:

    a = ${?a}foo
