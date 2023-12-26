using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI;
using Microsoft.SemanticKernel.Orchestration;
using SemanticKernel.Service.CopilotChat.Controllers;
using SemanticKernel.Service.CopilotChat.Plugins.ChatPlugins;
using SemanticKernel.Service.Models;
public class ChatService
{
    private readonly IKernel _chatKernel;
    private readonly Planner _chatPlanner;

    public const string SkillName = "ChatPlugin";
    public const string FunctionName = "Chat";

    public ChatService(IKernel chatKernel, Planner chatPlanner)
    {
        _chatKernel = chatKernel ?? throw new ArgumentNullException(nameof(chatKernel));
        _chatPlanner = chatPlanner ?? throw new ArgumentNullException(nameof(chatPlanner));
    }

    public async Task<ChatServiceResponse> ExecuteChatAsync(ChatRequest chatRequest)
    {
        var chatContext = CreateChatContext(chatRequest);

        ISKFunction? functionToInvoke = GetFunctionToInvoke(_chatKernel);
        if (functionToInvoke is null)
        {
            throw new Exception("Function to invoke is null.");
        }
        KernelResult chatResult = await ExecuteChatFunctionAsync(_chatKernel, chatContext, functionToInvoke);

        ChatServiceResponse resp = new ChatServiceResponse();
        resp.Result = chatResult;
        resp.ContextVariables = chatContext;

        return resp;
    }

    private ContextVariables CreateChatContext(ChatRequest chatRequest)
    {
        var chatContext = new ContextVariables(chatRequest.Input);
        foreach (var variable in chatRequest.Variables)
        {
            chatContext.Set(variable.Key, variable.Value);
        }

        return chatContext;
    }


    private ISKFunction? GetFunctionToInvoke(IKernel chatKernel)
    {
        return chatKernel.Skills.GetFunction(SkillName, FunctionName);
    }

    private async Task<KernelResult> ExecuteChatFunctionAsync(IKernel chatKernel, ContextVariables chatContext, ISKFunction functionToInvoke)
    {
        return await chatKernel.RunAsync(chatContext, functionToInvoke);
    }

    public ChatResponse CreateChatResponse(KernelResult chatResult, ContextVariables chatContext)
    {
        return new ChatResponse { Value = chatResult.GetValue<string>(), Variables = chatContext.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)) };
    }
}