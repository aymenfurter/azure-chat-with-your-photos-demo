using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI;
using Microsoft.SemanticKernel.Orchestration;
using SemanticKernel.Service.CopilotChat.Plugins.ChatPlugins;
using SemanticKernel.Service.Models;


namespace SemanticKernel.Service.CopilotChat.Controllers
{
    [ApiController]
    public class ChatMessageController : ControllerBase 
    {
        private readonly ILogger<ChatMessageController> logger;
        private readonly ChatService _chatService;

        public ChatMessageController(ILogger<ChatMessageController> logger, ChatService chatService)
        {
            this.logger = logger;
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        }

        [HttpPost]
        [Route("chat")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> HandleChatAsync([FromBody] ChatRequest chatRequest)
        {
            logger.LogDebug("Chat request received.");

            ChatServiceResponse chatResult = null;
            chatResult = await _chatService.ExecuteChatAsync(chatRequest);
     

            return Ok(CreateChatResponse(chatResult.Result, chatResult.ContextVariables));
        }


        private ChatResponse CreateChatResponse(KernelResult chatResult, ContextVariables vars)
        {
            return new ChatResponse { Value = chatResult.GetValue<string>(), Variables = vars.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)) };
        }
    }
}