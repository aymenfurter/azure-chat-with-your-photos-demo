using System.ComponentModel.DataAnnotations;
using SemanticKernel.Service.Options;

namespace SemanticKernel.Service.CopilotChat.Options;

public class PictureMemoryOptions
{
    public const string PropertyName = "PictureMemory";

    [Required, NotEmptyOrWhitespace]
    public string GlobalDocumentCollectionName { get; set; } = "embeddings";
}
