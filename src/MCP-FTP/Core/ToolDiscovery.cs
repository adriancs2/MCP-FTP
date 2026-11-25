using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MCP_FTP.Core
{
    public static class ToolDiscovery
    {
        public static Dictionary<string, ITool> DiscoverTools()
        {
            var tools = new Dictionary<string, ITool>();
            var assembly = Assembly.GetExecutingAssembly();

            var toolTypes = assembly.GetTypes()
                .Where(t => typeof(ITool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in toolTypes)
            {
                try
                {
                    var tool = (ITool)Activator.CreateInstance(type);
                    if (tool != null && !string.IsNullOrEmpty(tool.Name))
                        tools[tool.Name] = tool;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to instantiate tool {type.Name}: {ex.Message}");
                }
            }

            return tools;
        }

        public static List<McpToolDefinition> GenerateToolDefinitions(Dictionary<string, ITool> tools)
        {
            var definitions = new List<McpToolDefinition>();

            foreach (var tool in tools.Values)
            {
                var definition = new McpToolDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = new McpInputSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, McpPropertySchema>(),
                        Required = new List<string>()
                    }
                };

                foreach (var param in tool.Params.Parameters)
                {
                    var propertySchema = new McpPropertySchema
                    {
                        Type = param.Type,
                        Description = param.Description
                    };

                    if (param.EnumValues != null && param.EnumValues.Length > 0)
                        propertySchema.Enum = param.EnumValues;

                    if (param.Type == "array")
                        propertySchema.Items = new McpPropertySchema { Type = string.IsNullOrEmpty(param.ItemsType) ? "string" : param.ItemsType };

                    if (!string.IsNullOrEmpty(param.DefaultValue))
                    {
                        if (param.Type == "boolean" && bool.TryParse(param.DefaultValue, out bool bVal))
                            propertySchema.Default = bVal;
                        else if (param.Type == "integer" && int.TryParse(param.DefaultValue, out int iVal))
                            propertySchema.Default = iVal;
                        else
                            propertySchema.Default = param.DefaultValue;
                    }

                    definition.InputSchema.Properties[param.Name] = propertySchema;
                    if (param.Required)
                        definition.InputSchema.Required.Add(param.Name);
                }

                if (definition.InputSchema.Required.Count == 0)
                    definition.InputSchema.Required = null;

                definitions.Add(definition);
            }

            return definitions;
        }
    }
}
