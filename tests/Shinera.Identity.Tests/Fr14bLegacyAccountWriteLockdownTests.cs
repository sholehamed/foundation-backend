using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Modules.System.Identity.Web.Endpoints;
using Modules.System.Identity.Web.Util;
using Xunit;

namespace Shinera.Identity.Tests;

/// <summary>
/// The Tenant-list System Users routes still deny all old account lifecycle
/// writes. Admins can create *only inert* System identities through its
/// explicitly gated new writer; all other old lifecycle mutations remain 403.
/// </summary>
public sealed class Fr14bLegacyAccountWriteLockdownTests
{
    [Fact]
    public void System_users_legacy_lifecycle_operations_fail_closed()
    {
        var endpoints = new Users();
        Assert.IsType<ForbidHttpResult>(endpoints.UserCreate());
        Assert.IsType<ForbidHttpResult>(endpoints.UserUpdate(Guid.NewGuid()));
        Assert.IsType<ForbidHttpResult>(endpoints.UserDelete(Guid.NewGuid()));
        Assert.IsType<ForbidHttpResult>(endpoints.UserChangeState(Guid.NewGuid()));
        Assert.IsType<ForbidHttpResult>(endpoints.UserChangeLockState(Guid.NewGuid()));
    }

    [Fact]
    public void Inert_account_input_rejects_scope_credentials_and_privilege_fields()
    {
        var json = """
            {"userName":"operator","email":"operator@example.test",
             "firstName":"Safe","lastName":"User","isActive":true}
            """;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SystemPendingAccountWriter.CreateRequest>(
                json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        foreach (var forbidden in new[] { "tenantId", "password", "roleId", "userType" })
        {
            var payload = "{\"userName\":\"operator\",\"email\":\"operator@example.test\"," +
                          "\"firstName\":\"Safe\",\"lastName\":\"User\"," +
                          "\"" + forbidden + "\":\"forged\"}";
            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<SystemPendingAccountWriter.CreateRequest>(
                    payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
    }

    [Fact]
    public void System_admins_only_offer_a_separate_inert_create_contract()
    {
        var endpoints = new Admins();
        Assert.IsType<ForbidHttpResult>(endpoints.UserUpdate(Guid.NewGuid()));
        Assert.IsType<ForbidHttpResult>(endpoints.UserDelete(Guid.NewGuid()));
        Assert.IsType<ForbidHttpResult>(endpoints.UserChangeState(Guid.NewGuid()));
        Assert.IsType<ForbidHttpResult>(endpoints.UserChangeLockState(Guid.NewGuid()));

        var method = typeof(Admins).GetMethod(nameof(Admins.UserCreate));
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IResult>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Contains(parameters,
            p => p.ParameterType == typeof(SystemPendingAccountWriter.CreateRequest));
        Assert.DoesNotContain(parameters,
            p => p.ParameterType.Name.Contains("UserCreateCommand"));
    }
}
