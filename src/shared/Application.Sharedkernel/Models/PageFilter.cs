using Application.SharedKernel.Abstractions.Mapping;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Reflection;

namespace Application.SharedKernel.Models
{
    public record FilterModel<T> where T : class
    {
        public string? Search { get; set; }
        public int PageNumber { get; init; } = 1;
        public int PageSize { get; init; } = 10;
        public string SortBy { get; init; } = "CreatedAt";
        public bool IsDescending { get; init; } = true;
        public List<Filter> Filters { get; set; } = new();

       
    }

    public record PageFilter<T, DTO> :FilterModel<T> where T : class where DTO : class,IQueryFilter<T>
    {

        public async Task<PagedList<DTO>> ToPaging(IQueryable<T> Queryable, IMapper _mapper)
        {

            Queryable = FilterData(Queryable);
            Queryable = Sort(Queryable);

            return await Queryable.AsNoTracking()
                .ProjectTo<DTO>(_mapper.ConfigurationProvider)
                .PaginatedListAsync(PageNumber, PageSize);
        }


        private IQueryable<T> FilterData(IQueryable<T> Queryable)
        {
            foreach (var Filter in Filters)
            {
                if (string.IsNullOrWhiteSpace(Filter.Value)) continue;
                if(Filter.FilterBy== "__global")
                {
                     Queryable= DTO.Apply(Queryable, Filter.Value);
                    continue;
                }
                Filter.Value = Filter.Value.Trim();
                var reverse = false;
                switch (Filter.OperatorType.ToLower())
                {
                    case "startswith":
                        {
                            Filter.OperatorType = "StartsWith";
                            break;
                        }
                    case "contains":
                        {
                            Filter.OperatorType = "Contains";
                            break;
                        }
                    case "notcontains":
                        {
                            Filter.OperatorType = "Contains";
                            reverse = true;
                            break;
                        }
                    case "endswith":
                        {
                            Filter.OperatorType = "EndsWith";
                            break;
                        }
                    case "equals":
                        {
                            Filter.OperatorType = "==";
                            break;
                        }
                    case "notequals":
                        {
                            Filter.OperatorType = "!=";
                            break;
                        }
                    case "lt":
                        {
                            Filter.OperatorType = "<";
                            break;
                        }
                    case "lte":
                        {
                            Filter.OperatorType = "<=";
                            break;
                        }
                    case "gt":
                        {
                            Filter.OperatorType = ">";
                            break;
                        }
                    case "gte":
                        {
                            Filter.OperatorType = ">=";
                            break;
                        }

                    case "dateisnot":
                        {
                            Filter.OperatorType = "!=";
                            break;
                        }
                    case "dateis":
                        {
                            Filter.OperatorType = "==";
                            break;
                        }
                    case "datebefore":
                        {
                            Filter.OperatorType = "<=";
                            break;
                        }
                    case "dateafter":
                        {
                            Filter.OperatorType = ">=";
                            break;
                        }
                }

                switch (Filter.OperatorType)
                {
                    case "EndsWith":
                    case "StartsWith":
                    case "Contains":
                        {
                            var filterExpression =
                                GetFilterExpressionByStringMethods(Filter.FilterBy, Filter.Value, Filter.OperatorType,
                                    reverse);
                            Queryable = Queryable.Where(filterExpression);
                            break;
                        }
                    case "==":
                    case "!=":
                    case ">":
                    case ">=":
                    case "<":
                    case "<=":

                        {
                            var filterExpression =
                                GetFilterExpressionByComparisonOperators(Filter.FilterBy, Filter.Value, Filter.OperatorType);
                            Queryable = Queryable.Where(filterExpression);
                            break;
                        }
                }
            }

            return Queryable;
        }

        private IQueryable<T> Sort(IQueryable<T> dbset)
        {
            if (IsDescending) return dbset.OrderByDescending(GetSortExpression());

            return dbset.OrderBy(GetSortExpression());
        }


        private Expression<Func<T, object>> GetSortExpression()
        {
            // تقسیم نام ویژگی‌ها برای دسترسی به ویژگی‌های تو در تو
            var propertyNames = SortBy.Split('.').ToList();

            var parameter = Expression.Parameter(typeof(T), "x");
            Expression property = parameter;

            // عبور از هر قسمت از ویژگی‌های تو در تو
            foreach (var propertyName in propertyNames)
            {
                var propertyInfo = property.Type.GetProperty(propertyName,
                    BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

                if (propertyInfo == null)
                {
                    throw new ArgumentException($"Property '{propertyName}' not found in type '{property.Type.Name}'");
                }

                // به دست آوردن Expression برای ویژگی جاری
                property = Expression.Property(property, propertyInfo);
            }

            var conversion = Expression.Convert(property, typeof(object));

            return Expression.Lambda<Func<T, object>>(conversion, parameter);
        }

        

        private Expression<Func<T, bool>> GetFilterExpressionByStringMethods(string filterBy, string filterValue,string MethodName, bool reverse = false)
        {
            // تقسیم نام ویژگی‌ها بر اساس نقطه برای دسترسی به ویژگی‌های تو در تو
            var propertyNames = filterBy.Split('.').ToList();

            var parameter = Expression.Parameter(typeof(T), "x");
            Expression property = parameter;

            // عبور از هر قسمت از ویژگی‌های تو در تو
            foreach (var propertyName in propertyNames)
            {
                // دریافت PropertyInfo برای ویژگی جاری
                var propertyInfo = property.Type.GetProperty(propertyName,
                    BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

                if (propertyInfo == null)
                {
                    throw new ArgumentException($"Property '{propertyName}' not found in type '{property.Type.Name}'");
                }

                // به دست آوردن Expression برای ویژگی جاری
                property = Expression.Property(property, propertyInfo);
            }

            // اعمال متد رشته‌ای (مثل Contains، StartsWith، و غیره)
            var filterValueExpression = Expression.Constant(filterValue);
            var method = typeof(string).GetMethod(MethodName, new[] { typeof(string) });

            var filterExpression = Expression.Call(property, method, filterValueExpression);

            // اگر reverse باشد، فیلتر معکوس می‌شود
            if (reverse == false)
                return Expression.Lambda<Func<T, bool>>(filterExpression, parameter);

            return Expression.Lambda<Func<T, bool>>(Expression.Not(filterExpression), parameter);
        }


        private Expression<Func<T, bool>> GetFilterExpressionByComparisonOperators(string filterBy, string filterValue, string operatorType)
        {
            var propertyNames = filterBy.Split('.');
            var parameter = Expression.Parameter(typeof(T), "x");
            Expression property = parameter;

            // ساخت مسیر به ویژگی تو در تو
            foreach (var name in propertyNames)
            {
                var propertyInfo = property.Type.GetProperty(name,
                    BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

                if (propertyInfo == null)
                    throw new ArgumentException($"Property '{name}' not found in type '{property.Type.Name}'");

                property = Expression.Property(property, propertyInfo);
            }

            Expression comparisonExpression;

            // اگر نوع ویژگی DateTime یا DateTime? بود
            if (property.Type == typeof(DateTime) || property.Type == typeof(DateTime?))
            {
                var filterDate = DateTime.Parse(filterValue).Date;
                var filterDateExpression = Expression.Constant(filterDate, typeof(DateTime));

                Expression dateOnlyExpression;

                if (property.Type == typeof(DateTime?))
                {
                    var hasValue = Expression.Property(property, "HasValue");
                    var value = Expression.Property(property, "Value");
                    var date = Expression.Property(value, "Date");
                    dateOnlyExpression = date;

                    // اختیاری: می‌تونید بررسی null بودن رو هم اضافه کنید
                    // مثلاً فقط اگر HasValue == true باشه، فیلتر اعمال بشه
                }
                else
                {
                    dateOnlyExpression = Expression.Property(property, "Date");
                }

                comparisonExpression = operatorType switch
                {
                    "==" => Expression.Equal(dateOnlyExpression, filterDateExpression),
                    "!=" => Expression.NotEqual(dateOnlyExpression, filterDateExpression),
                    ">" => Expression.GreaterThan(dateOnlyExpression, filterDateExpression),
                    ">=" => Expression.GreaterThanOrEqual(dateOnlyExpression, filterDateExpression),
                    "<" => Expression.LessThan(dateOnlyExpression, filterDateExpression),
                    "<=" => Expression.LessThanOrEqual(dateOnlyExpression, filterDateExpression),
                    _ => throw new InvalidOperationException("عملگر نامعتبر")
                };
            }
            else if(property.Type == typeof(Guid?)|| property.Type == typeof(Guid))
            {
                object convertedValue = Guid.Parse(filterValue);
                var constant = Expression.Constant(convertedValue, property.Type);

                comparisonExpression = operatorType switch
                {
                    "==" => Expression.Equal(property, constant),
                    "!=" => Expression.NotEqual(property, constant),
                    ">" => Expression.GreaterThan(property, constant),
                    ">=" => Expression.GreaterThanOrEqual(property, constant),
                    "<" => Expression.LessThan(property, constant),
                    "<=" => Expression.LessThanOrEqual(property, constant),
                    _ => throw new InvalidOperationException("عملگر نامعتبر")
                };
            }
            else
            {
                // سایر انواع: عدد، رشته، بولین، و ...
                object convertedValue = Convert.ChangeType(filterValue, Nullable.GetUnderlyingType(property.Type) ?? property.Type);
                var constant = Expression.Constant(convertedValue, property.Type);

                comparisonExpression = operatorType switch
                {
                    "==" => Expression.Equal(property, constant),
                    "!=" => Expression.NotEqual(property, constant),
                    ">" => Expression.GreaterThan(property, constant),
                    ">=" => Expression.GreaterThanOrEqual(property, constant),
                    "<" => Expression.LessThan(property, constant),
                    "<=" => Expression.LessThanOrEqual(property, constant),
                    _ => throw new InvalidOperationException("عملگر نامعتبر")
                };
            }

            return Expression.Lambda<Func<T, bool>>(comparisonExpression, parameter);
        }


    }
  
    public record Filter
    {
        public string FilterBy { get; set; }
        public string Value { get; set; }
        public string OperatorType { get; set; }
    }
}
