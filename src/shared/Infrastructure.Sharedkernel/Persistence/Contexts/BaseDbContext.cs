using Application.SharedKernel.Abstractions.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.SharedKernel.Persistence.Contexts
{
    public class BaseDbContext : DbContext, IBaseDbContext
    {
        public BaseDbContext(DbContextOptions options) : base(options)
        {
        }
       
        protected BaseDbContext()
        {
        }
    }
}
