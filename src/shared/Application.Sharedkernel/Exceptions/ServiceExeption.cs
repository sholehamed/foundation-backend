namespace Application.SharedKernel.Exceptions;

[Serializable]
public class ServiceExeption : Exception
{
    public int ErrorCode;
    private string Message;

    public ServiceExeption(string message, int ErrorCode = 1) : base(message)
    {
        Message = message;
        this.ErrorCode = ErrorCode;
    }
}