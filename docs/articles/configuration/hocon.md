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

* A **field** is a key-value pair consisting a _key_, _separator_, and a _value_.
* A **separator** is the `:` or `=` character.
* A **key** is a string to the left of the _separator_.
* A **value** is any "value" to the right of the _separator_, as defined in the JSON spec, plus unquoted strings, triple quoted strings and substitutions.
* A **simple value** or a **literal value** is any value that are not objects or arrays.
* A **newline** is defined as the newline character `\n` (unicode value `0x000A`)
* References to a **file** ("the file being parsed") can be understood to mean any byte stream being parsed, not just literal files in a filesystem.

## Syntax

Much of this is defined with reference to JSON; you can find the JSON spec at <http://json.org/>.

### Unchanged From JSON

* files must be valid UTF-8
* quoted strings are in the same format as JSON strings
* values have possible types: string, number, object, array, boolean, null
* allowed number formats matches JSON.

### Comments

Anything between `//` or `#` and the next newline is considered a comment and ignored, unless the `//` or `#` is inside a quoted string.

### Omit Root Braces

JSON documents must have an array or object at the root. Empty
files are invalid documents, as are files containing only a
non-array non-object value such as a string.

In HOCON, if the file does not begin with a square bracket or
curly brace, it is parsed as if it were enclosed with `{}` curly
braces.

A HOCON file is invalid if it omits the opening `{` but still has
a closing `}`; the curly braces must be balanced.

### Key-Value Separator

You can use the `=` character instead of the standard JSON `:` separator.

If a key is followed by `{`, the `:` or `=` may be omitted. So `"foo" {}` means `"foo" : {}`

### Commas

Values in arrays, and fields in objects, need not have a comma between them as long as they have at least one ASCII newline (`\n`, unicode value `0x000A`) between them.

The last element in an array or last field in an object may be followed by a single comma. This extra comma is ignored.

These same comma rules apply to fields in objects.

Examples:

* `[1,2,3,]` and `[1,2,3]` are the same array.
* `[1\n2\n3]` and `[1,2,3]` are the same array.
* `[1,2,3,,]` is invalid because it has two trailing commas.
* `[,1,2,3]` is invalid because it has an initial comma.
* `[1,,2,3]` is invalid because it has two commas in a row.

### Duplicate Keys and Object Merging

Duplicate keys that are declared later in the string have different behaviors:

* A key with any values will override previous values if they are of different types.
* A key with literal or array value will override any previous key value.
* A key with object value will be recursively merged with previous object value.

Objects are merged by:

* Fields present in only one of the two objects are added to the merged object.
* Non-object fields in overriding object will override field with the same path on previous object.
* Object fields with the same path in both objects will be recursively merged according to these same rules.

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

#### Array and Object Concatenation

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

#### Note: Arrays without Commas or Newlines

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

### Path Expressions

Path expressions are used to write out a path through the object graph. They appear in two places; in substitutions, like `${foo.bar}`, and as the keys in objects like `{ foo.bar : 42 }`.

Path expressions are syntactically identical to a value concatenation, except that they may not contain substitutions. This means that you can't nest substitutions inside other substitutions, and you can't have substitutions in keys.

When concatenating the path expression, any `.` characters outside quoted strings are understood as path separators, while inside quoted strings `.` has no special meaning. So `foo.bar."hello.world"` would be a path with three elements, looking up key `foo`, key `bar`, then key `hello.world`.

### Paths as Keys

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

* `true : 42` is `"true" : 42`
* `3 : 42` is `"3" : 42`
* `3.14 : 42` is `"3" : { "14" : 42 }`

As a special rule, the unquoted string `include` may not begin a path expression in a key, because it has a special interpretation (see below).

### Substitutions

Substitutions are a way of referring to other parts of the configuration tree.

The syntax is `${pathexpression}` or `${?pathexpression}` where the `pathexpression` is a path expression as described above. This path expression has the same syntax that you could use for an object key.

* When you start a substitution with `${?`, the substitution is an _optional substitution_. The `?` in `${?pathexpression}` must not have whitespace before it; the three characters `${?` must be exactly like that, grouped together.
* A substitution without question mark (`${`) is called a _required substitution_.

Substitutions are not parsed inside quoted strings. To get a string containing a substitution, you must use value concatenation with the substitution in the unquoted portion:

    key : ${animal.favorite} is my favorite animal

Or you could quote the non-substitution portion:

    key : ${animal.favorite}" is my favorite animal"

Substitutions are resolved by looking up the path in the configuration. The path begins with the root configuration object, i.e. it is "absolute" rather than "relative."

Substitution processing is performed as the last parsing step, so a substitution can look forward in the configuration. If a configuration consists of multiple files, it may even end up retrieving a value from another file.

If a key has been specified more than once, the substitution will always evaluate to its latest-assigned value (that is, it will evaluate to the merged object, or the last non-object value that was set, in the entire document being parsed including all included files).

If a substitution does not match any value present in the configuration, then it is undefined. An undefined _required substitution_ is invalid and will generate an error.

[!code-csharp[SerializationSetupDocSpec](../../../src/core/Akka.Docs.Tests/Configuration/SerializationSetupDocSpec.cs?name=UnsolvableSubstitutionWillThrowSample)]

If an _optional substitution_ is undefined:

* If it is the value of an object field then the field would not be created. If the field would have overridden a previously-set value for the same field, then the previous value remains.
* If it is an array element then the element would not be added.
* If it is part of a value concatenation with another string then it would become an empty string; if part of a value concatenation with an object or array it would become an empty object or array.

`foo : ${?bar}` would avoid creating field `foo` if `bar` is undefined. `foo : ${?bar}${?baz}` would also avoid creating the field if _both_ `bar` and `baz` are undefined.

Substitutions are only allowed in field values and array elements (value concatenations), they are not allowed in keys or nested inside other substitutions (path expressions).

A substitution is replaced with any value type (number, object, string, array, true, false, null). If the substitution is the only part of a value, then the type is preserved. Otherwise, it is value-concatenated to form a string.

Examples:

[!code-csharp[SerializationSetupDocSpec](../../../src/core/Akka.Docs.Tests/Configuration/SerializationSetupDocSpec.cs?name=StringSubstitutionSample)]
[!code-csharp[SerializationSetupDocSpec](../../../src/core/Akka.Docs.Tests/Configuration/SerializationSetupDocSpec.cs?name=ArraySubstitutionSample)]
