using Microsoft.SemanticKernel;

namespace SemanticKernel.Service.CopilotChat.Plugins.ChatPlugins;

public class Planner
{
    public IKernel Kernel { get; }

    public Planner(IKernel plannerKernel)
    {
        this.Kernel = plannerKernel;
    }
}
