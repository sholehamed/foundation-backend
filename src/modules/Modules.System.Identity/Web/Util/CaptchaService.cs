using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SkiaSharp;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Modules.System.Identity.Web.Util
{
    public sealed class CaptchaService
    {
        private readonly IDataProtector _protector;
        private readonly string? _e2eCode;

        public CaptchaService(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("OfflineCaptcha.v1");
        }

        public CaptchaService(IDataProtectionProvider provider, IConfiguration configuration,
            IHostEnvironment environment) : this(provider)
        {
            // Fully opt-in, Development-only deterministic CAPTCHA for disposable CI.
            // This never changes CAPTCHA validation or provides a public bypass route.
            var code = configuration["Identity:E2E:CaptchaCode"];
            if (environment.IsDevelopment() &&
                configuration.GetValue<bool>("Identity:E2E:Enabled") &&
                configuration.GetValue<bool>("Identity:AllowInsecureHttpForE2E") &&
                code is { Length: 5 } &&
                code.All(c => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".Contains(c)))
            {
                _e2eCode = code;
            }
        }

        public CaptchaChallenge CreateChallenge()
        {
            var code = _e2eCode ?? GenerateCode(5);

            var payload = new CaptchaPayload
            {
                Code = code,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(3)
            };

            var json = JsonSerializer.Serialize(payload);
            var token = _protector.Protect(json);

            return new CaptchaChallenge(token);
        }

        public byte[] GenerateImage(string token)
        {
            var payload = ReadPayload(token);

            if (payload is null || payload.ExpiresAtUtc < DateTimeOffset.UtcNow)
                throw new InvalidOperationException("Captcha is expired or invalid.");

            return GenerateImageBytes(payload.Code);
        }

        public bool Validate(string token, string? userInput)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userInput))
                return false;

            var payload = ReadPayload(token);

            if (payload is null || payload.ExpiresAtUtc < DateTimeOffset.UtcNow)
                return false;

            return SecureEquals(
                payload.Code.ToUpperInvariant(),
                userInput.Trim().ToUpperInvariant()
            );
        }

        private static byte[] GenerateImageBytes(string code)
        {
            const int width = 170;
            const int height = 55;

            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);

            canvas.Clear(SKColors.White);

            DrawNoise(canvas, width, height);
            DrawText(canvas, code);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

            return data.ToArray();
        }

        private static void DrawText(SKCanvas canvas, string code)
        {
            using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
            using var font = new SKFont(typeface, 32)
            {
                Edging = SKFontEdging.Antialias
            };

            using var paint = new SKPaint
            {
                Color = SKColors.DarkBlue,
                IsAntialias = true
            };

            var random = RandomNumberGenerator.GetInt32(0, 8);

            for (var i = 0; i < code.Length; i++)
            {
                var x = 25 + i * 24;
                var y = 38 + RandomNumberGenerator.GetInt32(-3, 4);
                var angle = RandomNumberGenerator.GetInt32(-15, 16);

                canvas.Save();
                canvas.RotateDegrees(angle, x, y);
                canvas.DrawText(code[i].ToString(), x, y, SKTextAlign.Left, font, paint);
                canvas.Restore();
            }
        }

        private static void DrawNoise(SKCanvas canvas, int width, int height)
        {
            using var linePaint = new SKPaint
            {
                Color = SKColors.LightGray,
                StrokeWidth = 1,
                IsAntialias = true
            };

            for (var i = 0; i < 8; i++)
            {
                var x1 = RandomNumberGenerator.GetInt32(0, width);
                var y1 = RandomNumberGenerator.GetInt32(0, height);
                var x2 = RandomNumberGenerator.GetInt32(0, width);
                var y2 = RandomNumberGenerator.GetInt32(0, height);

                canvas.DrawLine(x1, y1, x2, y2, linePaint);
            }

            using var dotPaint = new SKPaint
            {
                Color = SKColors.Gray,
                IsAntialias = true
            };

            for (var i = 0; i < 60; i++)
            {
                var x = RandomNumberGenerator.GetInt32(0, width);
                var y = RandomNumberGenerator.GetInt32(0, height);

                canvas.DrawCircle(x, y, 1, dotPaint);
            }
        }

        private CaptchaPayload? ReadPayload(string token)
        {
            try
            {
                var json = _protector.Unprotect(token);
                return JsonSerializer.Deserialize<CaptchaPayload>(json);
            }
            catch
            {
                return null;
            }
        }

        private static string GenerateCode(int length)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

            var result = new char[length];

            for (var i = 0; i < length; i++)
            {
                var index = RandomNumberGenerator.GetInt32(chars.Length);
                result[i] = chars[index];
            }

            return new string(result);
        }

        private static bool SecureEquals(string a, string b)
        {
            var aBytes = Encoding.UTF8.GetBytes(a);
            var bBytes = Encoding.UTF8.GetBytes(b);

            return aBytes.Length == bBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }

        private sealed class CaptchaPayload
        {
            public string Code { get; set; } = "";
            public DateTimeOffset ExpiresAtUtc { get; set; }
        }
    }

    public sealed record CaptchaChallenge(string Token);
}