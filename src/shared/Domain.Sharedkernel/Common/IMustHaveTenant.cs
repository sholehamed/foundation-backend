namespace Domain.SharedKernel.Common
{
    public interface IMustHaveTenant
    {
        public Guid TenantId { get; set; }
    }
}
