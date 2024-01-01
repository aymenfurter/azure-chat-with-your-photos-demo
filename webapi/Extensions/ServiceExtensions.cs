// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AzureChatWithPhotos.Options;

namespace AzureChatWithPhotos.Extensions;

public static class ServiceExtensions
{

    public static IServiceCollection AddChatOptions(this IServiceCollection services, ConfigurationManager configuration)
    {
        services.AddOptions<PictureMemoryOptions>()
            .Bind(configuration.GetSection(PictureMemoryOptions.PropertyName))
            .ValidateOnStart()
            .PostConfigure(options => PropertyTrimmer.TrimStringProperties(options));

        services.AddOptions<PromptsOptions>()
            .Bind(configuration.GetSection(PromptsOptions.PropertyName))
            .ValidateOnStart()
            .PostConfigure(options => PropertyTrimmer.TrimStringProperties(options));

        return services;
    }
}