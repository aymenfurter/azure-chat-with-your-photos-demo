using System.ComponentModel.DataAnnotations;

namespace AzureChatWithPhotos.Options;

public class PictureMemoryOptions
{
    public const string PropertyName = "PictureMemory";

    [Required, NotEmptyOrWhitespace]
    public string GlobalDocumentCollectionName { get; set; } = "embeddings";
}
