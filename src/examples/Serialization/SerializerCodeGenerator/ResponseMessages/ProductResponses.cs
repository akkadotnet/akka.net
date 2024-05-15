//-----------------------------------------------------------------------
// <copyright file="ProductResponses.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Serialization;

namespace SerializerCodeGenerator.ResponseMessages;

[AkkaSerialize("pa")]
public class ProductAdded
{
    public long Id { get; set; }
}

[AkkaSerialize("p")]
public class Product
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Amount { get; set; }
}

[AkkaSerialize("pu")]
public class ProductUpdated
{
    public long Id { get; set; }
}

[AkkaSerialize("pd")]
public class ProductDeleted
{
    public long Id { get; set; }
}
