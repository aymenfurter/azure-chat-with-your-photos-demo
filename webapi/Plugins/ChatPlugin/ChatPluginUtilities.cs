using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AzureChatWithPhotos.Plugins.ChatPlugins;

public static class ChatPluginUtilities
{
    public static string ExtractChatHistory(string history, int tokenLimit)
    {
        if (history.Length > tokenLimit)
        {
            history = history.Substring(history.Length - tokenLimit);
        }

        return history;
    }

    public static List<string> ExtractLinks(string result, string chatContextText)
    {
        var lines = chatContextText.Split("\n");
        var links = new List<string>();
        string pattern = @"File:(.+)";
        foreach (var line in lines)
        {
            Match match = Regex.Match(line, pattern);
            if (match.Success)
            {
                var filename = match.Groups[1].Value.Trim();
                if (result.Contains(filename)) 
                {
                    links.Add($"/images/{filename}");
                }
            }
        }
        return links;
    }

    public static string ReplaceLinks(string result, List<string> imageFiles)
    {
        string updatedResult = result;
        foreach (string imageFile in imageFiles)
        {
            string pattern = $@"(?<!=""|')(?<!<a href[^>]*?){Regex.Escape(imageFile)}(?!=""|')(?!.*?</a>)";
            string replacement = $@"<a target=""_blank"" href=""/images/{imageFile}"">{imageFile}</a>";

            updatedResult = Regex.Replace(updatedResult, pattern, replacement);
        }
        return updatedResult;
    }
}
