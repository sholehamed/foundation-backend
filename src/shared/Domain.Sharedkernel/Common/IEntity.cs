namespace Domain.SharedKernel.Common
{
    public interface IEntity
    {
        public Guid Id { get;  }
        public byte[] RowVersion { get; set; }
    }
}
