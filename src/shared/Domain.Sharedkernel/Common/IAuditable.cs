namespace Domain.SharedKernel.Common
{
    public interface IAuditable
    {
        public Guid CreatedBy { get;  }
        public DateTime CreatedAt { get;  }
        public string? CreatedByIp { get;  }
        public Guid? LastModifiedBy { get; }
        public DateTime? LastModifiedAt { get;  }
        public string? LastModifiedByIp { get;  }
        void Create(Guid userId,string ip);
        void Modify(Guid userId, string ip);
    }
}
