using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Modules.System.Identity.Web.Util;
using Xunit;

public sealed class CaptchaServiceTests
{
    private readonly IDataProtectionProvider _provider = DataProtectionProvider.Create("ShineraIdentityTest");

    [Fact]
    public void Random_or_missing_captcha_is_rejected()
    {
        var service = new CaptchaService(_provider);
        Assert.False(service.Validate("", "ABCDE"));
        Assert.False(service.Validate("not-a-protected-token", "ABCDE"));
        Assert.False(service.Validate(service.CreateChallenge().Token, ""));
    }

    [Fact]
    public void Valid_token_accepts_correct_code_case_insensitively()
    {
        var service = new CaptchaService(_provider);
        var token = CreateToken("ABCDE", DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.True(service.Validate(token, " abcde "));
        Assert.False(service.Validate(token, "ABCDE1"));
    }

    [Fact]
    public void Expired_captcha_is_rejected()
    {
        var service = new CaptchaService(_provider);
        var token = CreateToken("ABCDE", DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.False(service.Validate(token, "ABCDE"));
    }

    private string CreateToken(string code, DateTimeOffset expiresAt)
        => _provider.CreateProtector("OfflineCaptcha.v1")
            .Protect(JsonSerializer.Serialize(new { Code = code, ExpiresAtUtc = expiresAt }));
}
