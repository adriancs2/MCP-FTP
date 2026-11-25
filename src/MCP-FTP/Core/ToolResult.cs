using System.Collections.Generic;

namespace MCP_FTP.Core
{
    public class ToolResult
    {
        public bool IsError { get; set; }
        public string Content { get; set; }
        public List<ToolContent> ContentList { get; set; }

        private ToolResult() { }

        public static ToolResult Success(string content)
        {
            return new ToolResult
            {
                IsError = false,
                Content = content,
                ContentList = new List<ToolContent> { new ToolContent { Type = "text", Text = content } }
            };
        }

        public static ToolResult Error(string message)
        {
            return new ToolResult
            {
                IsError = true,
                Content = message,
                ContentList = new List<ToolContent> { new ToolContent { Type = "text", Text = $"Error: {message}" } }
            };
        }

        public static ToolResult Error(string code, string message)
        {
            return new ToolResult
            {
                IsError = true,
                Content = $"[{code}] {message}",
                ContentList = new List<ToolContent> { new ToolContent { Type = "text", Text = $"Error [{code}]: {message}" } }
            };
        }
    }

    public class ToolContent
    {
        public string Type { get; set; }
        public string Text { get; set; }
    }
}
