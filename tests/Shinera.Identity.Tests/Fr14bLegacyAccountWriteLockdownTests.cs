using Microsoft.AspNetCore.Http;
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
