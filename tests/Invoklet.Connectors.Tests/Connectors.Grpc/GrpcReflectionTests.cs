using Invoklet.Connectors.Grpc;
using Xunit;

namespace Invoklet.Connectors.Tests.Connectors.Grpc;

// The server names the files Invoklet passes to protoc: a name must never become one of protoc's options or leave its folder.
[Trait("Platform", "Independent")]
public sealed class GrpcReflectionTests
{
    [Theory]
    [InlineData("inventory.proto", true)]
    [InlineData("inventory/v1/stock.proto", true)]
    [InlineData("google/protobuf/timestamp.proto", true)]
    [InlineData("--plugin=protoc-gen-grpc=/bin/sh", false)]
    [InlineData("--descriptor_set_out=/home/u/.bashrc.proto", false)]
    [InlineData("@args.proto", false)]
    [InlineData("/etc/passwd.proto", false)]
    [InlineData("../outside.proto", false)]
    [InlineData("a//b.proto", false)]
    [InlineData("inventory.txt", false)]
    public void IsSafeProtoPath_AFileNameFromTheServer_IsSafeOnlyAsARelativeProtoPath(string name, bool safe)
    {
        // Act
        var isSafe = GrpcReflection.IsSafeProtoPath(name);

        // Assert
        Assert.Equal(safe, isSafe);
    }
}
