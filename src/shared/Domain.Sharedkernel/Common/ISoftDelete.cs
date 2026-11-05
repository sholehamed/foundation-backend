namespace Domain.SharedKernel.Common
{
    public interface ISoftDelete
    {
        public bool IsDeleted { get; }
        public Guid? DeletedBy { get;  }
        public DateTime? DeletedAt { get;  }
        public string? DeletedByIp { get;  }
        void Delete(Guid userId, string ip);
    }
}
