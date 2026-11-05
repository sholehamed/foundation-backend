using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;
using Modules.System.Identity.Web.Util;

namespace Modules.System.Identity.Web.Endpoints;

/// <summary>
/// Public, anonymous provisioning boundary. NEVER call a privileged system TenantCreate endpoint.
/// Payment is intentionally separate: selection here confers no paid entitlement.
/// </summary>
public static partial class PublicSignup
{
    public static void Map(WebApplication app, string backendPrefix)
    {
        app.MapPost($"/{backendPrefix.Trim('/')}/public/signup/checkout", Register)
            .AllowAnonymous()
            .RequireRateLimiting("public-signup")
            .WithTags("Public Signup");
        app.MapGet($"/{backendPrefix.Trim('/')}/public/signup/slug-available", SlugAvailable)
            .AllowAnonymous()
            .RequireRateLimiting("public-signup")
            .WithTags("Public Signup");
    }

    private static async Task<IResult> SlugAvailable(string slug, IdentityDbContext db, CancellationToken ct)
    {
        slug = (slug ?? "").Trim().ToLowerInvariant();
        if (!ValidSlug().IsMatch(slug) || ReservedSlugs.Contains(slug))
            return Results.Ok(new { available = false });
        return Results.Ok(new { available = !await db.Tenants.AnyAsync(x => x.Slug == slug, ct) });
    }

    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "api", "app", "auth", "blog", "cdn", "help", "mail", "public",
        "root", "signup", "status", "support", "system", "www", "shinera"
    };
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidSlug();

    private static async Task<IResult> Register(
        SignupRequest input,
        IdentityDbContext db,
        ITenantContext tenantContext,
        IPasswordHasher<User> passwordHasher,
        CaptchaService captcha,
        CancellationToken ct)
    {
        var errors = Validate(input);
        if (errors.Count != 0) return Results.ValidationProblem(errors);

        if (!captcha.Validate(input.CaptchaToken!, input.CaptchaCode))
            return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["captchaCode"] = ["کد امنیتی نامعتبر یا منقضی شده است."]
            });

        var slug = input.Tenant!.Slug!.Trim().ToLowerInvariant();
        if (await db.Tenants.AnyAsync(x => x.Slug == slug, ct))
            return Results.Conflict(new { code = "slug_taken", message = "این آدرس فضای کاری قبلاً ثبت شده است." });

        var tenant = new Tenant
        {
            Name = input.Tenant.Name!.Trim(),
            Slug = slug,
            IsActive = true
        };
        var ownerInput = input.Owner!;
        var mobile = ownerInput.Mobile!.Trim();
        var user = new User(tenant.Id, mobile, ownerInput.Email!.Trim(),
            ownerInput.FirstName!.Trim(), ownerInput.LastName!.Trim(), UserType.Tenant)
        {
            NormalizedUserName = mobile.ToUpperInvariant(),
            NormalizedEmail = ownerInput.Email.Trim().ToUpperInvariant(),
            EmailConfirmed = false,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };
        user.PasswordHash = passwordHasher.HashPassword(user, ownerInput.Password!);

        var profileInput = input.BusinessProfile!;
        var branchInput = input.Branch;
        var profile = new BusinessProfile
        {
            TenantId = tenant.Id,
            DisplayName = profileInput.DisplayName!.Trim(),
            ActivityType = profileInput.ActivityType!.Trim(),
            Phone = NullIfBlank(profileInput.Phone),
            City = profileInput.City!.Trim(),
            Address = profileInput.Address!.Trim(),
            PostalCode = NullIfBlank(profileInput.PostalCode),
            Instagram = NullIfBlank(profileInput.Instagram),
            Culture = input.Tenant.Culture!,
            Timezone = input.Tenant.Timezone!,
            Currency = input.Tenant.Currency!,
            RequestedPlanKey = input.PlanKey!,
            RequestedBillingCycle = input.BillingCycle!,
            PromoCode = NullIfBlank(input.PromoCode),
            AcceptedTerms = true,
            AcceptedPrivacy = true,
            AcceptedAtUtc = DateTime.UtcNow
        };
        var mainBranch = new Branch
        {
            TenantId = tenant.Id,
            Name = NullIfBlank(branchInput?.Name) ?? "شعبه اصلی",
            Phone = NullIfBlank(branchInput?.Phone) ?? NullIfBlank(profileInput.Phone),
            City = NullIfBlank(branchInput?.City) ?? profileInput.City!.Trim(),
            Address = NullIfBlank(branchInput?.Address) ?? profileInput.Address!.Trim(),
            IsMain = true
        };
        var ownerRole = new Role(tenant.Id, "Owner", "مالک فضای کاری");
        var assignment = new UserRole(tenant.Id, user.Id, ownerRole.Id);

        // Only server-generated IDs enter this provisional scope. The scoped EF
        // write interceptor stays enabled, including for Role/UserRole/Branch/Profile.
        tenantContext.InitializeTenant(user.Id, tenant.Id, [tenant.Id], [tenant.Id], tenant.Id);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.Tenants.Add(tenant);
            db.Users.Add(user);
            db.BusinessProfiles.Add(profile);
            db.Branches.Add(mainBranch);
            db.Roles.Add(ownerRole);
            db.UserRoles.Add(assignment);
            // The owner User.TenantId and UserRole establish membership.
            // TenantAccessResolver grants home-tenant read/write automatically;
            // UserTenantAccess is reserved for *additional* cross-tenant grants.
            db.TenantClosure.Add(new TenantClosure {
                AncestorTenantId = tenant.Id,
                DescendantTenantId = tenant.Id,
                Depth = 0
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql &&
                                             (sql.Number == 2601 || sql.Number == 2627))
        {
            await transaction.RollbackAsync(ct);
            return Results.Conflict(new { code = "signup_conflict", message = "آدرس فضای کاری تکراری است. آدرس دیگری انتخاب کنید." });
        }

        return Results.Ok(new
        {
            tenantId = tenant.Id,
            ownerUserId = user.Id,
            mainBranchId = mainBranch.Id,
            slug = tenant.Slug,
            accountUsername = user.UserName,
            planStatus = "pending_activation"
        });
    }

    private static string? NullIfBlank(string? input) =>
        string.IsNullOrWhiteSpace(input) ? null : input.Trim();

    private static Dictionary<string, string[]> Validate(SignupRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        void Require(string name, string? value, int max) {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max)
                errors[name] = [$"این مقدار الزامی است و حداکثر {max} کاراکتر دارد."];
        }
        void Optional(string name, string? value, int max) {
            if (value?.Trim().Length > max) errors[name] = [$"حداکثر {max} کاراکتر مجاز است."];
        }

        Require("planKey", r.PlanKey, 20);
        if (r.PlanKey is not ("solo" or "solo-pro" or "salon" or "salon-pro"))
            errors["planKey"] = ["پلن انتخابی معتبر نیست."];
        if (r.BillingCycle is not ("monthly" or "yearly"))
            errors["billingCycle"] = ["دوره اشتراک نامعتبر است."];
        Optional("promoCode", r.PromoCode, 40);

        Require("tenant.name", r.Tenant?.Name, 120);
        Require("tenant.slug", r.Tenant?.Slug, 40);
        var slug = r.Tenant?.Slug?.Trim().ToLowerInvariant() ?? "";
        if (slug.Length < 3 || !ValidSlug().IsMatch(slug) || ReservedSlugs.Contains(slug))
            errors["tenant.slug"] = ["آدرس باید ۳ تا ۴۰ کاراکتر لاتین کوچک، عدد یا خط تیره و غیررزرو باشد."];
        if (r.Tenant?.Culture != "fa-IR" || r.Tenant.Timezone != "Asia/Tehran" || r.Tenant.Currency != "IRR")
            errors["tenant"] = ["تنظیمات منطقه‌ای پشتیبانی نمی‌شوند."];

        Require("owner.firstName", r.Owner?.FirstName, 60);
        Require("owner.lastName", r.Owner?.LastName, 80);
        if (!Regex.IsMatch(r.Owner?.Mobile ?? "", @"^09\d{9}$"))
            errors["owner.mobile"] = ["شماره موبایل معتبر وارد کنید."];
        Require("owner.email", r.Owner?.Email, 256);
        if (!MailAddress.TryCreate(r.Owner?.Email, out _))
            errors["owner.email"] = ["ایمیل معتبر وارد کنید."];
        if (r.Owner?.Password is not { Length: >= 8 and <= 128 })
            errors["owner.password"] = ["رمز عبور باید ۸ تا ۱۲۸ کاراکتر باشد."];

        Require("businessProfile.displayName", r.BusinessProfile?.DisplayName, 120);
        if (r.BusinessProfile?.ActivityType is not ("solo" or "salon" or "studio" or "clinic" or "other"))
            errors["businessProfile.activityType"] = ["نوع کسب‌وکار معتبر نیست."];
        Require("businessProfile.city", r.BusinessProfile?.City, 80);
        Require("businessProfile.address", r.BusinessProfile?.Address, 400);
        Optional("businessProfile.phone", r.BusinessProfile?.Phone, 20);
        Optional("businessProfile.postalCode", r.BusinessProfile?.PostalCode, 20);
        Optional("businessProfile.instagram", r.BusinessProfile?.Instagram, 80);

        Optional("branch.name", r.Branch?.Name, 120);
        Optional("branch.phone", r.Branch?.Phone, 20);
        Optional("branch.city", r.Branch?.City, 80);
        Optional("branch.address", r.Branch?.Address, 400);
        if (r.Branch is not null) {
            Require("branch.name", r.Branch.Name, 120);
            Require("branch.city", r.Branch.City, 80);
            Require("branch.address", r.Branch.Address, 400);
        }
        if (r.AcceptTerms != true || r.AcceptPrivacy != true)
            errors["consent"] = ["پذیرش قوانین و حریم خصوصی الزامی است."];
        Require("captchaToken", r.CaptchaToken, 8000);
        Require("captchaCode", r.CaptchaCode, 10);
        return errors;
    }

    public sealed record SignupRequest(
        string? PlanKey, string? BillingCycle, string? PromoCode,
        TenantInput? Tenant, BusinessInput? BusinessProfile, OwnerInput? Owner,
        BranchInput? Branch, bool AcceptTerms, bool AcceptPrivacy,
        string? CaptchaToken, string? CaptchaCode);
    public sealed record TenantInput(string? Name, string? Slug, string? Culture, string? Timezone, string? Currency);
    public sealed record BusinessInput(string? DisplayName, string? ActivityType, string? Phone,
        string? City, string? Address, string? PostalCode, string? Instagram);
    public sealed record OwnerInput(string? FirstName, string? LastName, string? Mobile, string? Email, string? Password);
    public sealed record BranchInput(string? Name, string? Phone, string? City, string? Address);
}
