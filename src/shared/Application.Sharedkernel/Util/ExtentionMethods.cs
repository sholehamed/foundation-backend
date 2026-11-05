using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Application.SharedKernel.Util
{
    public static class ExtentionMethods
    {
        public static IQueryable<T> WhereIf<T>(
       this IQueryable<T> source,
       bool condition,
       Expression<Func<T, bool>> predicate)
        {
            if (condition)
            {
                return source.Where(predicate);
            }
            return source;
        }

        // برای IEnumerable (مناسب برای لیست‌های درون حافظه)
        public static IEnumerable<T> WhereIf<T>(
            this IEnumerable<T> source,
            bool condition,
            Func<T, bool> predicate)
        {
            if (condition)
            {
                return source.Where(predicate);
            }
            return source;
        }
    }
}
