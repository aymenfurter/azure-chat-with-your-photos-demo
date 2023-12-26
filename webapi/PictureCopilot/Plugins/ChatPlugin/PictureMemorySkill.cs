using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Memory.AzureCognitiveSearchVector;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Orchestration;
using SemanticKernel.Service.CopilotChat.Options;
using SemanticKernel.Service.CopilotChat.Plugins.SortPlugin;

namespace SemanticKernel.Service.CopilotChat.Plugins.ChatPlugins
{
    public class PictureMemoryPlugin
    {
        private readonly PromptsOptions _promptOptions;
        private readonly PictureMemoryOptions _pictureImportOptions;
        private readonly AzureSearchMemoryClient _azureCognitiveSearchMemory;

        public PictureMemoryPlugin(
            IOptions<PromptsOptions> promptOptions,
            IOptions<PictureMemoryOptions> pictureImportOptions)
        {
            _promptOptions = promptOptions.Value;
            _pictureImportOptions = pictureImportOptions.Value;

            var acsEndpoint = Environment.GetEnvironmentVariable("ACS_INSTANCE");
            var acsApiKey = Environment.GetEnvironmentVariable("ACS_KEY");

            var searchEndpoint = $"https://{acsEndpoint}.search.windows.net/";


            HttpClient client = new HttpClient();
            _azureCognitiveSearchMemory = new AzureSearchMemoryClient(searchEndpoint, acsApiKey, client);
        }

        [SKFunction, Description("Query picture description in the memory given a user message")]
        //[SKParameter("tokenLimit", "Maximum number of tokens")]
        public async Task<string> QueryPictureVideosAsync([Description("Query to match.")] string query, SKContext context, IKernel kernel, SortPlugin.SortType sortType)
        {
            int tokenLimit = int.Parse(context.Variables["tokenLimit"], new NumberFormatInfo());
            var remainingToken = tokenLimit;

            var videoCollections = new[] { _pictureImportOptions.GlobalDocumentCollectionName };
            var relevantMemories = await GetRelevantMemories(query, videoCollections, sortType);
            var videosText = BuildDocumentText(ref remainingToken, relevantMemories);

            return string.IsNullOrEmpty(videosText)
                ? string.Empty
                : $"Here are relevant Picture snippets and IDs:\n{videosText}";
        }

        private async Task<List<MemoryQueryResult>> GetRelevantMemories(string query, string[] documentCollections, SortPlugin.SortType sortType)
        {
            var relevantMemories = new List<MemoryQueryResult>();

            foreach (var documentCollection in documentCollections)
            {
                var results = _azureCognitiveSearchMemory.SearchAsync(
                    documentCollection,
                    query,
                    sortType
                    );

                await foreach (var memory in results)
                {
                    relevantMemories.Add(memory);
                }
            }

            return relevantMemories.OrderByDescending(m => m.Relevance).ToList();
        }

        private static string BuildDocumentText(ref int remainingToken, List<MemoryQueryResult> relevantMemories)
        {
            var documentsText = string.Empty;

            foreach (var memory in relevantMemories)
            {
                var tokenCount = Utilities.TokenCount(memory.Metadata.Text);

                if (remainingToken - tokenCount <= 0)
                {
                    break;
                }

                documentsText += $"\n\n{memory.Metadata.Text}";
                remainingToken -= tokenCount;
            }

            return documentsText;
        }
    }
}