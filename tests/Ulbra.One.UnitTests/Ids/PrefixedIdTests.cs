using System.Text.Json;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.UnitTests.Ids;

public class PrefixedIdTests
{
    [Fact]
    public void ToString_Uses_Prefix_And_Compact_Guid()
    {
        var guid = Guid.NewGuid();
        var id = new CompanyId(guid);
        Assert.Equal("company_" + guid.ToString("N"), id.ToString());
    }

    [Fact]
    public void Parse_RoundTrips_From_ToString()
    {
        var id = new CompanyId(Guid.NewGuid());
        var parsed = CompanyId.Parse(id.ToString(), null);
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void TryParse_Accepts_Bare_Guid_Leniently()
    {
        var guid = Guid.NewGuid();
        Assert.True(CompanyId.TryParse(guid.ToString(), null, out var id));
        Assert.Equal(new CompanyId(guid), id);
    }

    [Fact]
    public void TryParse_Rejects_Garbage()
    {
        Assert.False(CompanyId.TryParse("not-an-id", null, out _));
        Assert.False(CompanyId.TryParse(null, null, out _));
        Assert.False(CompanyId.TryParse("", null, out _));
    }

    [Fact]
    public void JsonConverter_Writes_Prefixed_String()
    {
        var id = new VendorId(Guid.NewGuid());
        var json = JsonSerializer.Serialize(id);
        Assert.Equal($"\"{id}\"", json);
    }

    [Fact]
    public void JsonConverter_Reads_Prefixed_String()
    {
        var id = new VendorId(Guid.NewGuid());
        var json = JsonSerializer.Serialize(id);
        var back = JsonSerializer.Deserialize<VendorId>(json);
        Assert.Equal(id, back);
    }

    [Fact]
    public void JsonConverter_Reads_Bare_Guid_Leniently()
    {
        var guid = Guid.NewGuid();
        var back = JsonSerializer.Deserialize<VendorId>($"\"{guid}\"");
        Assert.Equal(new VendorId(guid), back);
    }
}
