using Microsoft.SemanticKernel.Orchestration;

namespace AzureChatWithPhotos.Services;

public class ChatServiceResponse
{
    public ContextVariables ContextVariables { get; set; }
    public KernelResult Result { get; set; }
}
