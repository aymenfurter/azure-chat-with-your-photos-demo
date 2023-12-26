using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI.TextCompletion;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel.TemplateEngine;
using SemanticKernel.Service.CopilotChat.Options;
using System.Text.RegularExpressions;
using System.IO;
using System;
using Microsoft.ApplicationInsights.AspNetCore.TelemetryInitializers;
using SemanticKernel.Service.CopilotChat.Plugins.SortPlugin;
using ClosedXML;
using Microsoft.SemanticKernel.TemplateEngine.Basic;
using Microsoft.SemanticKernel.Connectors.AI.OpenAI;
using System.Threading;
using Microsoft.SemanticKernel.AI;

namespace SemanticKernel.Service.CopilotChat.Plugins.ChatPlugins;

public class ChatPlugin
{
    private readonly IKernel _kernel;
    private readonly PromptsOptions _promptOptions;
    private readonly PictureMemoryPlugin _pictureMemorySkill;
    private readonly IKernel _plannerKernel;


    public ChatPlugin(
        IKernel kernel,
        IOptions<PromptsOptions> promptOptions,
        IOptions<PictureMemoryOptions> documentImportOptions,
        Planner planner,
        ILogger logger)
    {
        this._kernel = kernel;
        this._plannerKernel = planner.Kernel;
        this._promptOptions = promptOptions.Value;
        this._pictureMemorySkill = new PictureMemoryPlugin(
            promptOptions,
            documentImportOptions);
    }


    [SKFunction, Description("Extract user intent")]
    public async Task<string> ExtractUserIntentAsync(SKContext context)
    {
        var tokenLimit = this._promptOptions.CompletionTokenLimit;
        var historyTokenBudget =
            tokenLimit -
            this._promptOptions.ResponseTokenLimit -
            Utilities.TokenCount(string.Join("\n", new string[]
                {
                    this._promptOptions.SystemDescription,
                    this._promptOptions.SystemIntent,
                    this._promptOptions.SystemIntentContinuation
                })
            );

        var intentExtractionContext = context.Clone(); 
        intentExtractionContext.Variables.Set("tokenLimit", historyTokenBudget.ToString(new NumberFormatInfo()));

        var completionFunction = this._kernel.CreateSemanticFunction(
            this._promptOptions.SystemIntentExtraction,
            pluginName: nameof(ChatPlugin),
            description: "Complete the prompt.");

        var result = await completionFunction.InvokeAsync(
            intentExtractionContext,
            requestSettings: this.CreateIntentCompletionSettings()
        );

        return $"User intent: {result}";
    }
    
    [SKFunction, Description("Extract chat history")]
    public async Task<string> ExtractChatHistoryAsync(
        [Description("Chat history")] string history,
        [Description("Maximum number of tokens")] int tokenLimit)
    {
        if (history.Length > tokenLimit)
        {
            history = history.Substring(history.Length - tokenLimit);
        }

        return $"Chat history:\n{history}";
    }


     [SKFunction, Description("Get chat response")]
    public async Task<SKContext> ChatAsync(
        [Description("The new message")] string message,
        [Description("Previously proposed plan that is approved"), DefaultValue(null), SKName("proposedPlan")] string? planJson,
        [Description("ID of the response message for planner"), DefaultValue(null), SKName("responseMessageId")] string? messageId,
        SKContext context)
    {
        var chatContext = context.Clone();
        var history = context.Variables.ContainsKey("History")
            ? context.Variables["History"]
            : string.Empty;

        chatContext.Variables.Set("History", history + "\n" + message);


        var response = chatContext.Variables.ContainsKey("userCancelledPlan")
            ? "I am sorry the plan did not meet your goals."
            : await this.GetChatResponseAsync(chatContext);

        var prompt = chatContext.Variables.ContainsKey("prompt")
            ? chatContext.Variables["prompt"]
            : string.Empty;
        context.Variables.Set("prompt", prompt);

        var link = chatContext.Variables.ContainsKey("link")
            ? chatContext.Variables["link"]
            : string.Empty;
        context.Variables.Set("link", link);

        context.Variables.Update(response);
        return context;
    }

    #region Private

    private async Task<string> GetChatResponseAsync(SKContext chatContext)
    {
        var userIntent = await this.GetUserIntentAsync(chatContext);

        var remainingToken = this.GetChatContextTokenLimit(userIntent);

        var sortHandler = new SortHandler(this._kernel);
        var sortType = await sortHandler.ProcessUserIntent(userIntent);

        var pictureTransscriptContextTokenLimit = (int)(remainingToken * this._promptOptions.DocumentContextWeight);
        var pictureMemories = await this.QueryTransscriptsAsync(chatContext, userIntent, pictureTransscriptContextTokenLimit, _kernel, sortType);

        // Fill in chat history
        var chatContextComponents = new List<string>() { pictureMemories };
        var chatContextText = string.Join("\n\n", chatContextComponents.Where(c => !string.IsNullOrEmpty(c)));
        var chatContextTextTokenCount = remainingToken - Utilities.TokenCount(chatContextText);
        if (chatContextTextTokenCount > 0)
        {
            var chatHistory = await this.GetChatHistoryAsync(chatContext, chatContextTextTokenCount);
            chatContextText = $"{chatContextText}\n{chatHistory}";
        }


        chatContext.Variables.Set("UserIntent", userIntent);
        chatContext.Variables.Set("ChatContext", chatContextText);

        var promptTemplate = (new BasicPromptTemplateFactory()).Create(this._promptOptions.SystemChatPrompt, new PromptTemplateConfig());
        var renderedPrompt = await promptTemplate.RenderAsync(chatContext);


        var completionFunction = this._kernel.CreateSemanticFunction(
            renderedPrompt,
            pluginName: nameof(ChatPlugin),
            description: "Complete the prompt.");

        FunctionResult functionResult = await completionFunction.InvokeAsync(
            context: chatContext,
            requestSettings: this.CreateChatResponseCompletionSettings()
        );
        
        string functionResponse =  functionResult.GetValue<string>();
        List<string> pictureLinks = extractLinks(functionResponse, chatContextText);
        var result = replaceLinks(chatContext.Result, pictureLinks);
        chatContext.Variables.Set("link", string.Join("\n", pictureLinks));
        
        return result;
    }

    private static string replaceLinks(string result, List<string> imageFiles) {
        string updatedResult = result;
        foreach (string imageFile in imageFiles) {
            string pattern = $@"(?<!=""|')(?<!<a href[^>]*?){Regex.Escape(imageFile)}(?!=""|')(?!.*?</a>)";
            string replacement = $@"<a target=""_blank"" href=""/images/{imageFile}"">{imageFile}</a>";

            updatedResult = Regex.Replace(updatedResult, pattern, replacement);
        }
        return updatedResult;
    }

    private static List<string> extractLinks(string result, string chatContextText)
    {
        var lines = chatContextText.Split("\n");
        var links = new List<string>();
        string pattern = @"File:(.+)";
        foreach (var line in lines)
        {
            // print line for debug
            Match match = Regex.Match(line, pattern);
            if (match.Success)
            {
                var filename = match.Groups[1].Value;
                filename = filename.Trim();
                var link = $"/images/{filename}";

                if (result.Contains(filename)) {
                    links.Add(link);
                }
            }
        }

        return links;
    }
 

    private async Task<string> GetUserIntentAsync(SKContext context)
    {
        if (!context.Variables.TryGetValue("planUserIntent", out string? userIntent))
        {
            var contextVariables = new ContextVariables();

            SKContext intentContext = context.Clone();
            var history = context.Variables.ContainsKey("History")
                ? context.Variables["History"]
                : string.Empty;
            intentContext.Variables.Set("History", history);

            userIntent = await this.ExtractUserIntentAsync(intentContext);
            // Propagate the error
        }

        return userIntent;
    }


    private Task<string> GetChatHistoryAsync(SKContext context, int tokenLimit)
    {
        return this.ExtractChatHistoryAsync(context.Variables["History"], tokenLimit);
    }



    private Task<string> QueryTransscriptsAsync(SKContext context, string userIntent, int tokenLimit, IKernel kernel, SortPlugin.SortType sortType)
    {
        var pictureMemoriesContext = context.Clone();
        pictureMemoriesContext.Variables.Set("tokenLimit", tokenLimit.ToString(new NumberFormatInfo()));        

        var pictureMemories = this._pictureMemorySkill.QueryPictureVideosAsync(userIntent, pictureMemoriesContext, kernel, sortType);

        return pictureMemories;
    }

 
    private OpenAIRequestSettings CreateChatResponseCompletionSettings()
    {
        var completionSettings = new OpenAIRequestSettings
        {
            MaxTokens = this._promptOptions.ResponseTokenLimit,
            Temperature = this._promptOptions.ResponseTemperature,
            TopP = this._promptOptions.ResponseTopP,
            FrequencyPenalty = this._promptOptions.ResponseFrequencyPenalty,
            PresencePenalty = this._promptOptions.ResponsePresencePenalty
        };

        return completionSettings;
    }


    private OpenAIRequestSettings CreateIntentCompletionSettings()
    {
        var completionSettings = new OpenAIRequestSettings
        {
            MaxTokens = this._promptOptions.ResponseTokenLimit,
            Temperature = this._promptOptions.IntentTemperature,
            TopP = this._promptOptions.IntentTopP,
            FrequencyPenalty = this._promptOptions.IntentFrequencyPenalty,
            PresencePenalty = this._promptOptions.IntentPresencePenalty,
            StopSequences = new string[] { "] bot:" }
        };

        return completionSettings;
    }


    private int GetChatContextTokenLimit(string userIntent)
    {
        var tokenLimit = this._promptOptions.CompletionTokenLimit;
        var remainingToken =
            tokenLimit -
            Utilities.TokenCount(userIntent) -
            this._promptOptions.ResponseTokenLimit -
            Utilities.TokenCount(string.Join("\n", new string[]
                {
                            this._promptOptions.SystemDescription,
                            this._promptOptions.SystemResponse,
                            this._promptOptions.SystemChatContinuation
                })
            );

        return remainingToken;
    }

    # endregion
}