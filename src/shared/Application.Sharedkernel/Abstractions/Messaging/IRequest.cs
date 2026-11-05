namespace Application.SharedKernel.Abstractions.Messaging
{
    public interface IRequest
    {
    }
    public interface IRequest<out TResponse>
    {
    }
}
