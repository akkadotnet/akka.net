using Akka.Serialization;

namespace SerializerCodeGenerator;

[AkkaSerialize]
public class RemoteCommand
{
    public string Payload { get; set; } = string.Empty;
}

[AkkaSerialize]
public class MetadataData
{
}