using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.Tests;

public sealed class Fr14b3SystemAccountLifecycleContractTests
{
    private static IConfiguration Config(bool enabled)
        => new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Identity:PlatformAccounts:LifecycleEnabled"] = enabled.ToString()
            }).Build();

    [Fact]
    public void System_account_mutation_inputs_reject_mass_assignment()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.NotNull(JsonSerializer.Deserialize<
            SystemAccountLifecycleWriter.ProfileRequest>(
                """{"firstName":"Jane","lastName":"Doe","email":"jane@example.test","expectedVersion":"version-1"}""",
                options));
        foreach (var injected in new[]
        {
            """
            {"firstName":"Jane","lastName":"Doe","email":"jane@example.test",
             "expectedVersion":"version-1","tenantId":"foreign"}
            """,
            """
            {"firstName":"Jane","lastName":"Doe","email":"jane@example.test",
             "expectedVersion":"version-1","passwordHash":"forged"}
            """,
            """
            {"firstName":"Jane","lastName":"Doe","email":"jane@example.test",
             "expectedVersion":"version-1","userType":2}
            """,
            """
            {"firstName":"Jane","lastName":"Doe","email":"jane@example.test",
             "expectedVersion":"version-1","roleIds":[]}
            """
        })
            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<
                    SystemAccountLifecycleWriter.ProfileRequest>(injected, options));

        // Missing desired bool must NOT silently become false (suspend/lock).
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SystemAccountLifecycleWriter.StateRequest>(
                """{"expectedVersion":"v1"}""", options));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SystemAccountLifecycleWriter.LockRequest>(
                """{"expectedVersion":"v1"}""", options));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SystemAccountLifecycleWriter.StateRequest>(
                """{"isActive":true,"expectedVersion":"v1","isSuperAdmin":true}""",
                options));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SystemAccountLifecycleWriter.LockRequest>(
                """{"isLockedOut":false,"expectedVersion":"v1","securityStamp":"forged"}""",
                options));
    }

    [Fact]
    public async Task System_lifecycle_routes_are_off_by_default_and_https_required()
    {
        var endpoints = new Admins();
        var id = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        var disabled = Config(false);

        Assert.IsType<ForbidHttpResult>(await endpoints.GetSystemAccountLifecycle(
            http, null!, null!, disabled, id, CancellationToken.None));
        Assert.IsType<ForbidHttpResult>(await endpoints.UpdateSystemAccountProfile(
            http, null!, null!, disabled, id,
            new("A", "B", "a@example.test", "v1"), CancellationToken.None));
        Assert.IsType<ForbidHttpResult>(await endpoints.SetSystemAccountState(
            http, null!, null!, disabled, id,
            new(true, "v1"), CancellationToken.None));
        Assert.IsType<ForbidHttpResult>(await endpoints.SetSystemAccountLock(
            http, null!, null!, disabled, id,
            new(true, "v1"), CancellationToken.None));

        http.Request.Scheme = "http";
        var enabled = Config(true);
        Assert.IsType<ForbidHttpResult>(await endpoints.GetSystemAccountLifecycle(
            http, null!, null!, enabled, id, CancellationToken.None));

        // Legacy generic System User/Admins routes remain fail-closed.
        Assert.IsType<ForbidHttpResult>(endpoints.UserUpdate(id));
        Assert.IsType<ForbidHttpResult>(endpoints.UserChangeState(id));
        Assert.IsType<ForbidHttpResult>(endpoints.UserChangeLockState(id));
        Assert.IsType<ForbidHttpResult>(endpoints.UserDelete(id));
    }
}
