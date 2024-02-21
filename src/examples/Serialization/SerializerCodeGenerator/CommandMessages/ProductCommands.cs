//-----------------------------------------------------------------------
// <copyright file="ProductResponses.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Serialization;

namespace SerializerCodeGenerator.CommandMessages;

[AkkaSerialize("cp")]
public class CreateProduct
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Amount { get; set; }

    public override string ToString()
        => $"[CreateProduct] {{ Id:{Id}, Name:{Name}, Amount:{Amount} }}";
}

[AkkaSerialize("gp")]
public class GetProduct
{
    public long Id { get; set; }
}

[AkkaSerialize("up")]
public class UpdateProduct
{
    public long Id { get; set; }
    public int Amount { get; set; }
}

[AkkaSerialize("dp")]
public class DeleteProduct
{
    public long Id { get; set; }
}