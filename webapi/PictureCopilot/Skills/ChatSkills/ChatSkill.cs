﻿using System.Collections.Generic;
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
using Microsoft.SemanticKernel.SkillDefinition;
using Microsoft.SemanticKernel.TemplateEngine;
using SemanticKernel.Service.CopilotChat.Options;
using System.Text.RegularExpressions;
using System.IO;
using System;
using Microsoft.ApplicationInsights.AspNetCore.TelemetryInitializers;
using SemanticKernel.Service.CopilotChat.Skills.SortSkill;

namespace SemanticKernel.Service.CopilotChat.Skills.ChatSkills;

public class ChatSkill
{
    private readonly IKernel _kernel;
    private readonly PromptsOptions _promptOptions;
    private readonly PictureMemorySkill _pictureMemorySkill;
    private readonly IKernel _plannerKernel;


    public ChatSkill(
        IKernel kernel,
        IOptions<PromptsOptions> promptOptions,
        IOptions<PictureMemoryOptions> documentImportOptions,
        Planner planner,
        ILogger logger)
    {
        this._kernel = kernel;
        this._plannerKernel = planner.Kernel;
        this._promptOptions = promptOptions.Value;
        this._pictureMemorySkill = new PictureMemorySkill(
            promptOptions,
            documentImportOptions);
    }


    [SKFunction, Description("Extract user intent")]
    [SKParameter("chatId", "Chat ID to extract history from")]
    [SKParameter("audience", "The audience the chat bot is interacting with.")]
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
            skillName: nameof(ChatSkill),
            description: "Complete the prompt.");

        var result = await completionFunction.InvokeAsync(
            intentExtractionContext,
            settings: this.CreateIntentCompletionSettings()
        );

        if (result.ErrorOccurred)
        {
            context.Log.LogError("{0}: {1}", result.LastErrorDescription, result.LastException);
            context.Fail(result.LastErrorDescription);
            return string.Empty;
        }

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
        chatContext.Variables.Set("History", chatContext["History"] + "\n" + message);


        var response = chatContext.Variables.ContainsKey("userCancelledPlan")
            ? "I am sorry the plan did not meet your goals."
            : await this.GetChatResponseAsync(chatContext);

        if (chatContext.ErrorOccurred)
        {
            context.Fail(chatContext.LastErrorDescription);
            return context;
        }

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
        if (chatContext.ErrorOccurred)
        {
            return string.Empty;
        }

        var remainingToken = this.GetChatContextTokenLimit(userIntent);

        var sortHandler = new SortHandler(this._kernel);
        var sortType = await sortHandler.ProcessUserIntent(userIntent);

        var pictureTransscriptContextTokenLimit = (int)(remainingToken * this._promptOptions.DocumentContextWeight);
        var pictureMemories = await this.QueryTransscriptsAsync(chatContext, userIntent, pictureTransscriptContextTokenLimit, _kernel, sortType);
        if (chatContext.ErrorOccurred)
        {
            return string.Empty;
        }

        // Fill in chat history
        var chatContextComponents = new List<string>() { pictureMemories };
        var chatContextText = string.Join("\n\n", chatContextComponents.Where(c => !string.IsNullOrEmpty(c)));
        var chatContextTextTokenCount = remainingToken - Utilities.TokenCount(chatContextText);
        if (chatContextTextTokenCount > 0)
        {
            var chatHistory = await this.GetChatHistoryAsync(chatContext, chatContextTextTokenCount);
            if (chatContext.ErrorOccurred)
            {
                return string.Empty;
            }
            chatContextText = $"{chatContextText}\n{chatHistory}";
        }


        chatContext.Variables.Set("UserIntent", userIntent);
        chatContext.Variables.Set("ChatContext", chatContextText);

        var promptRenderer = new PromptTemplateEngine();
        var renderedPrompt = await promptRenderer.RenderAsync(
            this._promptOptions.SystemChatPrompt,
            chatContext);


        var completionFunction = this._kernel.CreateSemanticFunction(
            renderedPrompt,
            skillName: nameof(ChatSkill),
            description: "Complete the prompt.");

        chatContext = await completionFunction.InvokeAsync(
            context: chatContext,
            settings: this.CreateChatResponseCompletionSettings()
        );
        
        List<string> pictureLinks = extractLinks(chatContext.Result, chatContextText);
        var result = chatContext.Result;
        chatContext.Variables.Set("link", string.Join("\n", pictureLinks));
        
        chatContext.Log.LogInformation("Prompt: {0}", renderedPrompt);

        if (chatContext.ErrorOccurred)
        {
            return string.Empty;
        }


        return result;
    }

 
    // FIXME: This method needs cleanup
    private static List<string> extractLinks(string result, string chatContextText)
    {
        var regex = new Regex(@"\bhttps?://\S+");
        var matches = regex.Matches(result);
        var pictureLinks = new List<string>();

        foreach (Match match in matches)
        {
            var url = match.Value;
            url = url.Replace(",", "");
            url = url.Replace(")", "");
            url = url.Replace(".", "");
            url = url.Replace("]", "");
            pictureLinks.Add(url);
        }

        if (pictureLinks.Count == 0)
        {
            var matchesContext = regex.Matches(chatContextText);

            foreach (Match match in matchesContext)
            {
                if (pictureLinks.Count < 3) {
                    var url = match.Value;
                    url = url.Replace(",", "");
                    url = url.Replace(")", "");
                    url = url.Replace(".", "");
                    url = url.Replace("]", "");
                    pictureLinks.Add(url);
                }
            }

        }

        return pictureLinks;
    }


    private async Task<string> GetUserIntentAsync(SKContext context)
    {
        if (!context.Variables.TryGetValue("planUserIntent", out string? userIntent))
        {
            var contextVariables = new ContextVariables();

            SKContext intentContext = context.Clone();
            intentContext.Variables.Set("History", context["History"]);

            userIntent = await this.ExtractUserIntentAsync(intentContext);
            // Propagate the error
            if (intentContext.ErrorOccurred)
            {
                context.Fail(intentContext.LastErrorDescription);
            }
        }

        // log user intent
        context.Log.LogInformation("User intent: {0}", userIntent);

        return userIntent;
    }


    private Task<string> GetChatHistoryAsync(SKContext context, int tokenLimit)
    {
        return this.ExtractChatHistoryAsync(context["History"], tokenLimit);
    }



    private Task<string> QueryTransscriptsAsync(SKContext context, string userIntent, int tokenLimit, IKernel kernel, SortSkill.SortType sortType)
    {
        var pictureMemoriesContext = context.Clone();
        pictureMemoriesContext.Variables.Set("tokenLimit", tokenLimit.ToString(new NumberFormatInfo()));        

        var pictureMemories = this._pictureMemorySkill.QueryPictureVideosAsync(userIntent, pictureMemoriesContext, kernel, sortType);

        if (pictureMemoriesContext.ErrorOccurred)
        {
            context.Fail(pictureMemoriesContext.LastErrorDescription);
        }

        return pictureMemories;
    }

 
    private CompleteRequestSettings CreateChatResponseCompletionSettings()
    {
        var completionSettings = new CompleteRequestSettings
        {
            MaxTokens = this._promptOptions.ResponseTokenLimit,
            Temperature = this._promptOptions.ResponseTemperature,
            TopP = this._promptOptions.ResponseTopP,
            FrequencyPenalty = this._promptOptions.ResponseFrequencyPenalty,
            PresencePenalty = this._promptOptions.ResponsePresencePenalty
        };

        return completionSettings;
    }


    private CompleteRequestSettings CreateIntentCompletionSettings()
    {
        var completionSettings = new CompleteRequestSettings
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