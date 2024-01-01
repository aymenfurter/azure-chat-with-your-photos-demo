using System;

namespace AzureChatWithPhotos.Exceptions
{
    public class BlobNotFoundException : Exception
    {
        public BlobNotFoundException(string message) : base(message)
        {
        }
    }
}
