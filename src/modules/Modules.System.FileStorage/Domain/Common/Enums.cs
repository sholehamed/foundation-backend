namespace Modules.System.FileStorage.Domain.Common
{
    public enum MediaType
    {
        Image = 1,
        Video = 2,
        File = 3
    }
    public enum MediaProcessingStatus
    {
        Pending = 0,
        Processing = 1,
        Ready = 2,
        Failed = 3
    }
    public enum MediaVariantType
    {
        Original = 1,
        Optimized = 2,
        Thumbnail = 3
    }
}
