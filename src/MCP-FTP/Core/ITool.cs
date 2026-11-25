using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace MCP_FTP.Core
{
    public interface ITool
    {
        string Name { get; }
        string Description { get; }
        ToolParamList Params { get; }
        ToolResult Execute(JObject args);
    }

    public class ToolParamList
    {
        private readonly List<ToolParam> _params = new List<ToolParam>();
        public List<ToolParam> Parameters => _params;

        public ToolParamList String(string name, string description, bool required = false, string defaultValue = null)
        {
            _params.Add(new ToolParam { Name = name, Type = "string", Description = description, Required = required, DefaultValue = defaultValue });
            return this;
        }

        public ToolParamList StringEnum(string name, string description, string[] enumValues, bool required = false, string defaultValue = null)
        {
            _params.Add(new ToolParam { Name = name, Type = "string", Description = description, Required = required, DefaultValue = defaultValue, EnumValues = enumValues });
            return this;
        }

        public ToolParamList Bool(string name, string description, bool required = false, bool? defaultValue = null)
        {
            _params.Add(new ToolParam { Name = name, Type = "boolean", Description = description, Required = required, DefaultValue = defaultValue?.ToString().ToLower() });
            return this;
        }

        public ToolParamList Int(string name, string description, bool required = false, int? defaultValue = null)
        {
            _params.Add(new ToolParam { Name = name, Type = "integer", Description = description, Required = required, DefaultValue = defaultValue?.ToString() });
            return this;
        }

        public ToolParamList Array(string name, string description, bool required = false, string itemsType = "string")
        {
            _params.Add(new ToolParam { Name = name, Type = "array", Description = description, Required = required, ItemsType = itemsType });
            return this;
        }
    }

    public class ToolParam
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public string DefaultValue { get; set; }
        public string[] EnumValues { get; set; }
        public string ItemsType { get; set; }
    }
}
