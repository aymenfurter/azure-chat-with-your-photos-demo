using Microsoft.SemanticKernel.Orchestration;

public class ChatServiceResponse
{
    public ContextVariables ContextVariables { get; set; }
    public KernelResult Result { get; set; }
}
