using Xunit;

public sealed class TenantContextTests
{
    [Fact]
    public void System_user_has_no_tenant_and_no_implicit_access()
    {
        var context = new TenantContext();
        var userId = Guid.NewGuid();

        context.InitializeSystem(userId, isSuperAdmin: true);

        Assert.True(context.IsSystemContext);
        Assert.True(context.IsSuperAdmin);
        Assert.Equal(userId, context.UserId);
        Assert.Null(context.ActiveTenantId);
        Assert.Null(context.HomeTenantId);
        Assert.Empty(context.ReadableTenantIds);
        Assert.Empty(context.WritableTenantIds);
        Assert.False(context.IsFilterDisabled);
    }

    [Fact]
    public void Tenant_user_has_explicit_read_and_write_scope()
    {
        var context = new TenantContext();
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();

        context.InitializeTenant(
            Guid.NewGuid(),
            tenantId,
            [tenantId, otherTenant],
            [tenantId],
            tenantId);

        Assert.False(context.IsSystemContext);
        Assert.False(context.IsSuperAdmin);
        Assert.Equal(tenantId, context.HomeTenantId);
        Assert.Equal(tenantId, context.ActiveTenantId);
        Assert.Contains(otherTenant, context.ReadableTenantIds);
        Assert.DoesNotContain(otherTenant, context.WritableTenantIds);
    }

    [Fact]
    public void Filter_override_is_scoped_and_reentrant()
    {
        var context = new TenantContext();
        Assert.False(context.IsFilterDisabled);

        using (context.DisableFilter())
        {
            Assert.True(context.IsFilterDisabled);
            using (context.DisableFilter())
                Assert.True(context.IsFilterDisabled);
            Assert.True(context.IsFilterDisabled);
        }

        Assert.False(context.IsFilterDisabled);
    }
}
