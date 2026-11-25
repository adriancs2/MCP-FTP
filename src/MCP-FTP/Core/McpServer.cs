using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MCP_FTP.Core
{
    public class McpServer
    {
        public const string ServerName = "mcp-ftp";

        private static readonly string[] SupportedProtocolVersions =
            { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };

        private readonly Dictionary<string, ITool> _tools;
        private readonly object _writeLock = new object();
        private bool _initialized;

        public McpServer()
        {
            _tools = ToolDiscovery.DiscoverTools();
        }

        public void Run()
        {
            while (true)
            {
                string line;
                try
                {
                    line = Console.In.ReadLine();
                }
                catch
                {
                    break;
                }

                if (line == null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var request = JsonConvert.DeserializeObject<JsonRpcRequest>(line);
                    HandleRequest(request);
                }
                catch (JsonException ex)
                {
                    SendError(null, JsonRpcErrorCodes.ParseError, "Parse error", ex.Message);
                }
                catch (Exception ex)
                {
                    SendError(null, JsonRpcErrorCodes.InternalError, "Internal error", ex.Message);
                }
            }
        }

        private void HandleRequest(JsonRpcRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Method))
            {
                SendError(request?.Id, JsonRpcErrorCodes.InvalidRequest, "Invalid request", null);
                return;
            }

            bool isNotification = request.Id == null;

            try
            {
                switch (request.Method)
                {
                    case "initialize":
                        HandleInitialize(request);
                        break;
                    case "tools/list":
                        HandleToolsList(request);
                        break;
                    case "tools/call":
                        HandleToolCall(request);
                        break;
                    case "ping":
                        if (!isNotification) SendResponse(request.Id, new { });
                        break;
                    default:
                        if (!isNotification)
                            SendError(request.Id, JsonRpcErrorCodes.MethodNotFound, $"Method not found: {request.Method}", null);
                        break;
                }
            }
            catch (Exception ex)
            {
                if (!isNotification)
                    SendError(request.Id, JsonRpcErrorCodes.InternalError, "Internal error", ex.Message);
            }
        }

        private void HandleInitialize(JsonRpcRequest request)
        {
            _initialized = true;
            string requested = request.Params?.Value<string>("protocolVersion");
            string version = SupportedProtocolVersions.Contains(requested) ? requested : SupportedProtocolVersions[0];
            var v = Assembly.GetExecutingAssembly().GetName().Version;

            var result = new InitializeResult
            {
                ProtocolVersion = version,
                Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
                ServerInfo = new ServerInfo { Name = ServerName, Version = $"{v.Major}.{v.Minor}.{v.Build}" },
                Instructions = "MCP FTP Auto-Sync server. Credentials are DPAPI-encrypted and never exposed to the AI. Use profile names only. To delete remote files, write relative paths (one per line) to profiles/{profile}/delete-file.txt and run ftp_sync, or call ftp_delete_remote_files."
            };

            SendResponse(request.Id, result);
        }

        private void HandleToolsList(JsonRpcRequest request)
        {
            if (!_initialized)
            {
                SendError(request.Id, JsonRpcErrorCodes.InvalidRequest, "Server not initialized", null);
                return;
            }
            SendResponse(request.Id, new ToolsListResult { Tools = ToolDiscovery.GenerateToolDefinitions(_tools) });
        }

        private void HandleToolCall(JsonRpcRequest request)
        {
            if (!_initialized)
            {
                SendError(request.Id, JsonRpcErrorCodes.InvalidRequest, "Server not initialized", null);
                return;
            }

            string toolName = request.Params?.Value<string>("name");
            if (string.IsNullOrEmpty(toolName) || !_tools.TryGetValue(toolName, out ITool tool))
            {
                SendError(request.Id, JsonRpcErrorCodes.InvalidParams, $"Unknown tool: {toolName}", null);
                return;
            }

            JObject args = request.Params["arguments"] as JObject ?? new JObject();
            ToolResult toolResult;
            try
            {
                toolResult = tool.Execute(args);
            }
            catch (ArgumentException ex)
            {
                toolResult = ToolResult.Error("INVALID_PARAMS", ex.Message);
            }
            catch (Exception ex)
            {
                toolResult = ToolResult.Error("INTERNAL_ERROR", ex.Message);
            }

            var mcpResult = new ToolCallResult
            {
                Content = toolResult.ContentList.Select(c => new ToolContentItem
                {
                    Type = c.Type,
                    Text = c.Text
                }).ToList(),
                IsError = toolResult.IsError ? (bool?)true : null
            };

            SendResponse(request.Id, mcpResult);
        }

        private void SendResponse(object id, object result)
        {
            WriteLine(JsonConvert.SerializeObject(new JsonRpcResponse { Id = id, Result = result }));
        }

        private void SendError(object id, int code, string message, object data)
        {
            WriteLine(JsonConvert.SerializeObject(new JsonRpcResponse
            {
                Id = id,
                Error = new JsonRpcError { Code = code, Message = message, Data = data }
            }));
        }

        private void WriteLine(string json)
        {
            lock (_writeLock)
            {
                Console.Out.Write(json);
                Console.Out.Write('\n');
                Console.Out.Flush();
            }
        }
    }
}
