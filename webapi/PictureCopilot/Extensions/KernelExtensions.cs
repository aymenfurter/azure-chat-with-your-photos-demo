using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Memory.AzureCognitiveSearch;
using Microsoft.SemanticKernel.Plugins.Core;
using SemanticKernel.Service.CopilotChat.Options;
using SemanticKernel.Service.CopilotChat.Plugins.ChatPlugins;
using SemanticKernel.Service.Options;

namespace SemanticKernel.Service.CopilotChat.Extensions;


public static class KernelExtensions
{

    public static IServiceCollection AddPlannerServices(this IServiceCollection services)
    {
        services.AddScoped<Planner>(sp => new Planner(Kernel.Builder
            .WithPlannerBackend()
            .Build()));

        return services;
    }


    public static IKernel RegisterSkills(this IKernel kernel, IServiceProvider sp)
    {
        kernel.ImportSkill(new ChatPlugin(
                kernel: kernel,
                promptOptions: sp.GetRequiredService<IOptions<PromptsOptions>>(),
                documentImportOptions: sp.GetRequiredService<IOptions<PictureMemoryOptions>>(),
                planner: sp.GetRequiredService<Planner>(),
                logger: sp.GetRequiredService<ILogger<ChatPlugin>>()),
            nameof(ChatPlugin));

        kernel.ImportSkill(new TimePlugin(), nameof(TimePlugin));
        return kernel;
    }


    private static KernelBuilder WithPlannerBackend(this KernelBuilder kernelBuilder)
    {
        kernelBuilder.WithAzureOpenAIChatCompletionService(Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME"), Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"), Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY"));
        return kernelBuilder;
    }
}