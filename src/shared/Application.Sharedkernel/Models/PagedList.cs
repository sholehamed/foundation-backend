namespace Application.SharedKernel.Models
{
    public record PagedList<T>
    {
        public PagedList(IReadOnlyCollection<T> Data, int count, int pageNumber, int pageSize)
        {
            PageNumber = pageNumber;
            TotalPages = (int)Math.Ceiling(count / (double)pageSize);
            TotalCount = count;
            this.Data = Data;
        }

        public IReadOnlyCollection<T> Data { get; }
        public int PageNumber { get; }
        public int TotalPages { get; }
        public int TotalCount { get; }

        public bool HasPreviousPage => PageNumber > 1;

        public bool HasNextPage => PageNumber < TotalPages;

        
    }
}
