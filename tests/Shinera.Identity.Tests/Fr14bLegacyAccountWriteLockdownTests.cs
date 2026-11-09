using Microsoft.AspNetCore.Http.HttpResults;
using Modules.System.Identity.Web.Endpoints;
using Xunit;

namespace Shinera.Identity.Tests;

/// <summary>
/// FR-14B-1 regression: neither privileged HTTP route family may dispatch the
/// legacy unscoped create/profile/password/delete/state/lock CQRS commands.
/// The separately audited role and permission grant routes are not blocked.
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
    public void System_admins_legacy_lifecycle_operations_fail_closed()
    {
        var endpoints = new Admins();
        Assert.IsType<ForbidHttpResult>(endpoints.UserCreate());
        Assert.IsType<ForbidHttpResult>(endpoints.UserUpdate(Guid.NewGuid()));
        Assert.IsType<ForbidHttpResult>(endpoints.UserDelete(Guid.NewGuid()));
        Assert.IsType<ForbidHttpResult>(endpoints.UserChangeState(Guid.NewGuid()));
        Assert.IsType<ForbidHttpResult>(endpoints.UserChangeLockState(Guid.NewGuid()));
    }
}
