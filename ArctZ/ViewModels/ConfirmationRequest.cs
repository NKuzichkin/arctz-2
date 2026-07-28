using System.Threading.Tasks;

namespace ArctZ.ViewModels;

public sealed class ConfirmationRequest
{
    internal ConfirmationRequest(string message, TaskCompletionSource<bool> completion)
    {
        Message = message;
        Completion = completion;
    }

    public string Message { get; }

    internal TaskCompletionSource<bool> Completion { get; }
}
