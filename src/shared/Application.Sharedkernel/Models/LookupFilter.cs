namespace Application.SharedKernel.Models
{
    public record LookupFilter
    {
        public string? Text { get; set; }
        public Guid? ParentId { get; set; }
    }
}
