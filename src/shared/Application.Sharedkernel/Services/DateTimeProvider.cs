using Application.SharedKernel.Abstractions;

namespace Application.SharedKernel.Services
{
    public sealed class DateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
