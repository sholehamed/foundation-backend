using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.System.Bff;
using Xunit;

namespace Foundation.Bff.Tests;

public sealed class BffAtomicStoreStartupGuardTests
{
    [Theory]
    [InlineData(Environments.Production)]
    [InlineData(Environments.Staging)]
    public void Non_development_bff_startup_rejects_plain_distributed_cache(string environment)
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddLogging();

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddBffAuthentication(ValidConfiguration(enabled: true),
                new FakeEnvironment(environment)));

        Assert.Contains(nameof(IBffAtomicTicketStore), error.Message);
        Assert.Contains("IDistributedCache alone is not sufficient", error.Message);
    }

    [Fact]
    public void Production_bff_startup_rejects_scoped_atomic_store_registration()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddLogging();
        services.AddScoped<IBffAtomicTicketStore, StubAtomicStore>();

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddBffAuthentication(ValidConfiguration(enabled: true),
                new FakeEnvironment(Environments.Production)));
        Assert.Contains("singleton", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_bff_uses_the_registered_atomic_provider_not_the_default_store()
    {
        var services = new ServiceCollection();
        // SQL-backed implementations do not need IDistributedCache.
        services.AddLogging();
        services.AddSingleton<IBffAtomicTicketStore, StubAtomicStore>();

        services.AddBffAuthentication(ValidConfiguration(enabled: true),
            new FakeEnvironment(Environments.Production));

        using var provider = services.BuildServiceProvider();
        var atomic = provider.GetRequiredService<IBffAtomicTicketStore>();
        Assert.IsType<StubAtomicStore>(atomic);
        Assert.Same(atomic, provider.GetRequiredService<ITicketStore>());
    }

    [Fact]
    public void Development_bff_can_use_default_tombstone_backed_ticket_store()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBffAuthentication(ValidConfiguration(enabled: true),
            new FakeEnvironment(Environments.Development));

        using var provider = services.BuildServiceProvider();
        Assert.IsType<DistributedBffTicketStore>(
            provider.GetRequiredService<ITicketStore>());
    }

    [Fact]
    public void Disabled_bff_never_requires_atomic_provider_or_oidc_configuration()
    {
        var services = new ServiceCollection();
        services.AddBffAuthentication(
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Identity:Bff:Enabled"] = "false" })
                .Build(),
            new FakeEnvironment(Environments.Production));

        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<ITicketStore>());
    }

    private static IConfiguration ValidConfiguration(bool enabled)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:Bff:Enabled"] = enabled.ToString(),
            ["Identity:Bff:Authority"] = "https://identity.example.com",
            ["Identity:Bff:ClientId"] = "foundation-browser-bff",
            ["Identity:Bff:ClientSecret"] = "test-only-secret-longer-than-thirty-two-characters",
            ["Tenant:WorkspaceBaseDomain"] = "shinera.localhost",
            ["Identity:Bff:RedirectUris:0"] = "https://shinera.localhost/signin-oidc"
        }).Build();

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Foundation.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// A DI marker stub ONLY. It is deliberately incapable of serving tickets;
    /// tests verify registration and startup, not provider atomicity.
    /// A real implementation must have provider-specific concurrency tests.
    /// </summary>
    private sealed class StubAtomicStore : IBffAtomicTicketStore
    {
        public Task<string> StoreAsync(AuthenticationTicket ticket)
            => throw new NotSupportedException("Test-only stub");
        public Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken token)
            => throw new NotSupportedException("Test-only stub");

        public Task RenewAsync(string key, AuthenticationTicket ticket)
            => throw new NotSupportedException("Test-only stub");
        public Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken token)
            => throw new NotSupportedException("Test-only stub");

        public Task<AuthenticationTicket?> RetrieveAsync(string key)
            => throw new NotSupportedException("Test-only stub");
        public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken token)
            => throw new NotSupportedException("Test-only stub");

        public Task RemoveAsync(string key)
            => throw new NotSupportedException("Test-only stub");
        public Task RemoveAsync(string key, CancellationToken token)
            => throw new NotSupportedException("Test-only stub");
    }
}
