using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Sends secrets only to the inactive member's registered email; never returns
/// invitation tokens to an Owner, browser API, logs or SQL. Production delivery
/// is disabled by default and requires a separately provisioned TLS SMTP relay.
/// </summary>
public interface ITenantInvitationSender
{
    bool IsConfigured { get; }
    Task SendAsync(string recipientEmail, string tenantSlug, string token,
        CancellationToken cancellationToken);
}

public sealed class SmtpTenantInvitationSender(IConfiguration configuration)
    : ITenantInvitationSender
{
    private readonly string? host = configuration["Identity:TenantInvitations:Smtp:Host"];
    private readonly string? user = configuration["Identity:TenantInvitations:Smtp:User"];
    private readonly string? password = configuration["Identity:TenantInvitations:Smtp:Password"];
    private readonly string? from = configuration["Identity:TenantInvitations:Smtp:From"];
    private readonly string? workspaceBaseDomain = configuration["Tenant:WorkspaceBaseDomain"];
    private readonly int port = configuration.GetValue<int>(
        "Identity:TenantInvitations:Smtp:Port", 587);
    private readonly bool enabled = configuration.GetValue<bool>(
        "Identity:TenantInvitations:Enabled");

    // Strict DNS-only hostnames. No scheme, port, userinfo, path or fragments.
    private static readonly Regex DnsName = new(
        @"\A[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TenantSlug = new(
        @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public bool IsConfigured =>
        enabled && !string.IsNullOrWhiteSpace(host) &&
        !string.IsNullOrWhiteSpace(user) &&
        !string.IsNullOrWhiteSpace(password) &&
        !string.IsNullOrWhiteSpace(from) &&
        !string.IsNullOrWhiteSpace(workspaceBaseDomain) &&
        port is >= 1 and <= 65535 &&
        DnsName.IsMatch(workspaceBaseDomain) &&
        MailAddress.TryCreate(from, out _) &&
        MailAddress.TryCreate(user, out _);

    public async Task SendAsync(string recipientEmail, string tenantSlug,
        string token, CancellationToken cancellationToken)
    {
        if (!IsConfigured || !TenantSlug.IsMatch(tenantSlug) ||
            !MailAddress.TryCreate(recipientEmail, out var destination) ||
            !Regex.IsMatch(token, @"\A[A-F0-9]{64}\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Tenant invitation delivery is not available.");

        var url = new UriBuilder(Uri.UriSchemeHttps,
            tenantSlug + "." + workspaceBaseDomain)
        {
            Path = "/auth/activate",
            // Fragment is not sent to the origin/proxy during first-page GET.
            // Angular must read it only in memory and clear the address bar
            // immediately after capture; never persist in localStorage.
            Fragment = "token=" + token
        }.Uri.AbsoluteUri;

        using var message = new MailMessage(
            new MailAddress(from!), destination)
        {
            Subject = "فعال‌سازی حساب فضای کاری",
            Body = "برای فعال‌سازی حساب کاربری فضای کاری، لینک زیر را باز کنید.\n" +
                   "این لینک حداکثر ۳۰ دقیقه معتبر و فقط یک‌بار قابل استفاده است.\n" +
                   url + "\nاگر این درخواست را نمی‌شناسید آن را نادیده بگیرید.",
            IsBodyHtml = false
        };
        using var smtp = new SmtpClient(host!, port)
        {
            Credentials = new NetworkCredential(user, password),
            EnableSsl = true, // TLS verification remains enabled; no fallback to plain SMTP.
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        await smtp.SendMailAsync(message, cancellationToken);
    }
}
