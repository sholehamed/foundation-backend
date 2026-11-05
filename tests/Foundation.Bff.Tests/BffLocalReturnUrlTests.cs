using Modules.System.Bff.Security;
using Xunit;

namespace Foundation.Bff.Tests;

public sealed class BffLocalReturnUrlTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/dashboard", "/dashboard")]
    [InlineData("/system/dashboard", "/system/dashboard")]
    [InlineData("/services/11223344-5566-7788-99aa-bbccddeeff00", "/services/11223344-5566-7788-99aa-bbccddeeff00")]
    [InlineData("/dashboard?tab=appointments", "/dashboard?tab=appointments")]
    [InlineData("/dashboard?name=Ali%20Reza", "/dashboard?name=Ali%20Reza")]
    public void Accepts_only_same_origin_rooted_paths(string url, string expected)
    {
        Assert.True(BffLocalReturnUrl.TryValidate(url, out var result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.example")]
    [InlineData("http://evil.example")]
    [InlineData("//evil.example/login")]
    [InlineData("///evil.example")]
    [InlineData("dashboard")]
    [InlineData("../dashboard")]
    [InlineData("/x/../admin")]
    [InlineData("/x/./admin")]
    [InlineData("/x/..")]
    [InlineData("/x//admin")]
    [InlineData("/%2f%2fevil.example")]
    [InlineData("/x/%2e%2e/y")]
    [InlineData("/x/%255c/y")]
    [InlineData("/\\evil.example")]
    [InlineData("/x#fragment")]
    [InlineData("/x\r\nSet-Cookie: x=1")]
    [InlineData("/dashboard?x=%0d%0aInjected")]
    [InlineData("/dashboard?x=%5cbackslash")]
    public void Rejects_unsafe_or_external_navigation(string? url)
    {
        Assert.False(BffLocalReturnUrl.TryValidate(url, out var result));
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Rejects_overlong_value()
    {
        var url = "/" + new string('a', BffLocalReturnUrl.MaxLength);
        Assert.False(BffLocalReturnUrl.TryValidate(url, out _));
    }

    [Fact]
    public void Does_not_decode_or_rewrite_the_accepted_query()
    {
        const string url = "/dashboard?filter=2026%2D11%2D06&lang=fa";
        Assert.True(BffLocalReturnUrl.TryValidate(url, out var result));
        Assert.Equal(url, result);
    }
}
