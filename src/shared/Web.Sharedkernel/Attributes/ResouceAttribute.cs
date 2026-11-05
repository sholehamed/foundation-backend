namespace Web.SharedKernel.Attributes
{
    public class ResouceAttribute:Attribute
    {
        public ResouceAttribute(string summary, string description)
        {
            Summary = summary;
            Description = description;
        }

        public string Summary { get; }
        public string Description { get; }
    }
}
