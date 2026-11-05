using System;
using System.Collections.Generic;
using System.Text;

namespace Application.SharedKernel.Abstractions
{
    public interface IDateTimeProvider
    {
        DateTime UtcNow { get; }

    }
}
