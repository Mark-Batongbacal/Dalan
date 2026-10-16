using Dalan.Domain.Vehicles;
using Xunit;

namespace Dalan.UnitTests;

public sealed class VehicleTests
{
    [Fact]
    public void Registration_normalizes_and_assigns_distinct_ids()
    {
        var first = Vehicle.Register("  abc-123  ");
        var second = Vehicle.Register("abc-123");

        Assert.Equal("ABC-123", first.RegistrationNumber);
        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567")]
    public void Invalid_registration_is_rejected(string? value)
        => Assert.Throws<ArgumentException>(() => Vehicle.Register(value));

    [Fact]
    public void Maximum_length_is_checked_after_trimming()
        => Assert.Equal(32, Vehicle.Register("  " + new string('a', 32) + "  ").RegistrationNumber.Length);
}
