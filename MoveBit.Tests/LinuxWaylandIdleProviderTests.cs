using MoveBit.Services;
using Xunit;

namespace MoveBit.Tests;

public sealed class LinuxWaylandIdleProviderTests
{
    [Theory]
    [InlineData("t 12345", 12345UL)]
    [InlineData("(uint64 987654,)", 987654UL)]
    [InlineData("u 42", 42UL)]
    [InlineData("(uint32 17,)", 17UL)]
    public void Parses_numeric_dbus_payloads(string output, ulong expected)
    {
        Assert.True(LinuxWaylandIdleProvider.TryParseFirstUnsigned(output, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("error")]
    [InlineData("uint64 nope")]
    public void Rejects_outputs_without_a_number(string output)
    {
        Assert.False(LinuxWaylandIdleProvider.TryParseFirstUnsigned(output, out _));
    }
}
