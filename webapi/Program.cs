// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using TeamsBot;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AzureChatWithPhotos.Extensions;
using Azure.Storage.Blobs;
using AzureChatWithPhotos.Services;

namespace SemanticKernel.Service;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Host.AddConfiguration();

        builder.Services
            .AddSingleton<ILogger>(sp => sp.GetRequiredService<ILogger<Program>>()) // some services require an un-templated ILogger
            .AddOptions(builder.Configuration)
            .AddSemanticKernelServices();

        builder.Services
            .AddChatOptions(builder.Configuration)
            .AddTransient<ChatService>();

        builder.Services
            .AddHttpClient()
            .AddControllers()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.MaxDepth = HttpHelper.BotMessageSerializerSettings.MaxDepth;
            });

        builder.Services.
            AddSingleton<BlobServiceClient>(provider => {
                string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
                return new BlobServiceClient(connectionString);
            })
            .AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>()
            .AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>()
            .AddSingleton<BlobStorageService>()
            .AddSingleton<IStorage, MemoryStorage>()
            .AddSingleton<ConversationState>()
            .AddTransient<IBot, AzureChatWithPhotos.Bots.TeamsBot>();

        builder.Services
            .AddApplicationInsightsTelemetry()
            .AddLogging(logBuilder => logBuilder.AddApplicationInsights())
            .AddEndpointsApiExplorer()
            .AddSwaggerGen()
            .AddCors();


        WebApplication app = builder.Build();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        Task runTask = app.RunAsync();

        try
        {
            string? address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
            app.Services.GetRequiredService<ILogger>().LogInformation("Health probe: {0}/probe", address);
        }
        catch (ObjectDisposedException)
        {
            // We likely failed startup which disposes 'app.Services' - don't attempt to display the health probe URL.
        }

        await runTask;
    }
}
