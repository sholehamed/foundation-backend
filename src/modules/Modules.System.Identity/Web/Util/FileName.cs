using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Modules.System.Identity.Web.Util
{
    public sealed class CurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUser(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        // بررسی وجود User در HttpContext و احراز هویت شده بودن آن
        public bool IsAuthenticated =>
            _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

        public Guid UserId
        {
            get
            {
                var id = _httpContextAccessor.HttpContext?.User?.FindFirstValue(OpenIddictConstants.Claims.Subject);

                if (string.IsNullOrEmpty(id))
                    return Guid.Empty;

                return Guid.TryParse(id, out var guid) ? guid : Guid.Empty;
            }
        }

        public Guid HomeTenantId
        {
            get
            {
                // فرض می‌کنیم در کلیم‌ها با نام "tenant_id" یا مشابه ذخیره شده است
                var tenantId = _httpContextAccessor.HttpContext?.User?.FindFirstValue("home_tenant_id");

                if (string.IsNullOrEmpty(tenantId))
                    return Guid.Empty;

                return Guid.TryParse(tenantId, out var guid) ? guid : Guid.Empty;
            }
        }

        public string? UserName => _httpContextAccessor.HttpContext?.User?.Identity?.Name;

        // استخراج User Agent از هدرهای HTTP
        public string? UserAgent => _httpContextAccessor!.HttpContext?.Request?.Headers["User-Agent"].ToString();

        // استخراج IP Address با در نظر گرفتن سناریوهای Proxy/Forward
        public string? IpAddress
        {
            get
            {
                var context = _httpContextAccessor.HttpContext;
                if (context == null) return null;

                // ابتدا چک کردن هدر X-Forwarded-For برای زمانی که پشت پروکسی هستیم
                string? ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

                if (string.IsNullOrWhiteSpace(ip))
                {
                    // اگر پروکسی نبود، از RemoteIpAddress استفاده می‌کنیم
                    ip = context.Connection.RemoteIpAddress?.ToString();
                }
                else
                {
                    // هدر X-Forwarded-For ممکن است شامل لیستی از IPها باشد (IP1, IP2, ...)
                    // اولین IP در لیست، IP واقعی کاربر است.
                    ip = ip.Split(',').FirstOrDefault()?.Trim();
                }

                return ip;
            }
        }
        public IEnumerable<Claim> GetClaims() =>
            _httpContextAccessor.HttpContext?.User?.Claims ?? Enumerable.Empty<Claim>();
    }

}
