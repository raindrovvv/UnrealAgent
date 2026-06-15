using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Mcp;
using UnrealAgent.Backend.Security;
using UnrealAgent.Backend.Tool.Attributes;
using UnrealAgent.Backend.Tool.Tools;

namespace UnrealAgent.Backend.Tool;
using AnthropicTool = Anthropic.Models.Messages.Tool;
using ClrType = System.Type;

/// <summary>
/// [AgentTool] 어트리뷰트를 스캔하여 도구를 등록하고 실행합니다.
/// 도구 인스턴스는 Discovery 시 한 번 생성되어 재사용됩니다.
/// </summary>
public sealed class ToolRegistry(IServiceProvider ServiceProvider)
{
    /// <summary>도구 인스턴스와 Claude API 스키마를 묶어 보관합니다.</summary>
    private sealed record ToolEntry(IAgentTool Tool, AnthropicTool Schema);

    /// <summary>도구 이름 → ToolEntry 매핑입니다.</summary>
    private readonly Dictionary<string, ToolEntry> Tools = new();

    /// <summary>Lazy 도구 정의 — 첫 호출 전까지 스텁 스키마로만 노출됩니다.</summary>
    private readonly Dictionary<string, LazyToolDefinition> LazyDefinitions = new();

    /// <summary>첫 호출 시 활성화된 Lazy 도구 인스턴스입니다.</summary>
    private readonly Dictionary<string, ToolEntry> ActivatedLazy = new();

    /// <summary>
    /// Claude API에 전달할 도구 목록을 반환합니다.
    /// ActiveTools(풀 스키마) + LazyTools(스텁 스키마) + 이미 활성화된 Lazy 도구 포함.
    /// </summary>
    public IReadOnlyList<AnthropicTool> GetToolsForClaude()
        => BuildClaudeTools(null);

    /// <summary>
    /// 사용자 입력에 맞는 MCP 카테고리만 우선 노출합니다. 애매하면 전체 도구를 보존합니다.
    /// </summary>
    public IReadOnlyList<AnthropicTool> GetToolsForClaude(UserInput? Input)
        => BuildClaudeTools(Input);

    private IReadOnlyList<AnthropicTool> BuildClaudeTools(UserInput? Input)
    {
        List<AnthropicTool> Result = Tools.Values.Select(E => E.Schema).ToList();

        // Lazy 도구: 이미 활성화된 것은 풀 스키마, 미활성화는 스텁 스키마
        foreach (KeyValuePair<string, LazyToolDefinition> Kv in LazyDefinitions)
        {
            if (ActivatedLazy.TryGetValue(Kv.Key, out ToolEntry? Active))
                Result.Add(Active.Schema);
            else
                Result.Add(Kv.Value.ToStubTool());
        }

        return FilterToolsForInput(Result, Input);
    }

    /// <summary>시스템 프롬프트에 넣을 등록된 MCP 도구 이름 목록입니다.</summary>
    public IReadOnlyList<string> GetMcpToolNames()
    {
        return Tools.Keys
            .Concat(LazyDefinitions.Keys)
            .Where(Name => Name.StartsWith("mcp__", StringComparison.Ordinal))
            .Select(Name =>
            {
                int LastSeparator = Name.LastIndexOf("__", StringComparison.Ordinal);
                return LastSeparator >= 0 ? Name[(LastSeparator + 2)..] : Name;
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(Name => Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// OpenAI 호환 API에 전달할 도구 목록을 반환합니다 (function calling 포맷).
    /// GetToolsForClaude와 같은 도구 집합을 OpenAI 스키마로 변환합니다.
    /// </summary>
    public List<System.Text.Json.Nodes.JsonObject> GetToolsForOpenAI()
        => GetToolsForOpenAI(null);

    /// <summary>
    /// OpenAI 호환 API에 전달할 도구 목록을 반환합니다 (function calling 포맷).
    /// 사용자 입력에 따라 MCP 도구 카테고리를 보수적으로 줄입니다.
    /// </summary>
    public List<System.Text.Json.Nodes.JsonObject> GetToolsForOpenAI(UserInput? Input)
    {
        List<System.Text.Json.Nodes.JsonObject> Result = [];

        foreach (AnthropicTool Schema in GetToolsForClaude(Input).OrderBy(Schema => GetOpenAIToolPriority(Schema.Name)))
        {
            System.Text.Json.Nodes.JsonObject Properties = [];
            foreach ((string Key, JsonElement Value) in Schema.InputSchema.Properties ?? new Dictionary<string, JsonElement>())
                Properties[Key] = System.Text.Json.Nodes.JsonNode.Parse(Value.GetRawText());

            System.Text.Json.Nodes.JsonArray Required = [];
            foreach (string Name in Schema.InputSchema.Required ?? [])
                Required.Add(Name);

            Result.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "function",
                ["function"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["name"] = Schema.Name,
                    ["description"] = Schema.Description ?? "",
                    ["parameters"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = Properties,
                        ["required"] = Required
                    }
                }
            });
        }

        return Result;
    }

    private static List<AnthropicTool> FilterToolsForInput(List<AnthropicTool> Schemas, UserInput? Input)
    {
        if (Input is null || string.IsNullOrWhiteSpace(Input.Text))
            return Schemas;

        HashSet<string> Categories = InferMcpCategories(Input.Text);
        if (Categories.Count == 0)
            return Schemas;

        List<AnthropicTool> Filtered = Schemas
            .Where(Schema => !Schema.Name.StartsWith("mcp__", StringComparison.Ordinal) ||
                             ShouldKeepMcpTool(Schema.Name, Categories))
            .ToList();

        // If the category classifier misses every MCP tool, keep the full set.
        // This preserves tool availability for new/renamed MCP tools instead of silently removing all editor control.
        return Filtered.Any(Schema => Schema.Name.StartsWith("mcp__", StringComparison.Ordinal))
            ? Filtered
            : Schemas;
    }

    private static HashSet<string> InferMcpCategories(string Text)
    {
        string Lower = Text.ToLowerInvariant();
        HashSet<string> Categories = [];

        void AddIf(bool Condition, params string[] Values)
        {
            if (!Condition) return;
            foreach (string Value in Values)
                Categories.Add(Value);
        }

        AddIf(ContainsAny(Lower, "레벨", "월드", "level", "world", "map"), "level", "actor", "python");
        AddIf(ContainsAny(Lower, "액터", "배치", "스폰", "선택", "actor", "place", "spawn", "select"), "actor", "level", "python");
        AddIf(ContainsAny(Lower, "에셋", "콘텐츠", "asset", "content"), "asset", "python");
        AddIf(ContainsAny(Lower, "블루프린트", "bp_", "blueprint"), "blueprint", "asset", "python");
        AddIf(ContainsAny(Lower, "위젯", "umg", "ui", "widget", "wbp"), "umg", "widget", "blueprint", "python");
        AddIf(ContainsAny(Lower, "머티리얼", "material", "mi_", "m_"), "material", "asset", "python");
        AddIf(ContainsAny(Lower, "나이아가라", "niagara", "vfx"), "niagara", "asset", "python");
        AddIf(ContainsAny(Lower, "파이썬", "python", "스크립트"), "python");

        return Categories;
    }

    private static bool ShouldKeepMcpTool(string ToolName, HashSet<string> Categories)
    {
        string Lower = ToolName.ToLowerInvariant();
        if (Categories.Contains("python") && Lower.Contains("python"))
            return true;

        return Categories.Any(Category => Lower.Contains(Category, StringComparison.Ordinal));
    }

    private static bool ContainsAny(string Text, params string[] Needles)
        => Needles.Any(Text.Contains);

    private static int GetOpenAIToolPriority(string Name)
    {
        if (Name.StartsWith("mcp__", StringComparison.Ordinal)) return 0;
        if (Name == "skill") return 2;
        return 1;
    }

    /// <summary>도구를 이름으로 실행합니다. Lazy 도구는 첫 호출 시 투명하게 활성화됩니다.</summary>
    public async Task<ToolResult> ExecuteAsync(string Name, string InputJson, AgentSession Session, CancellationToken Ct = default)
    {
        // Lazy 도구 첫 호출: 활성화 후 실행
        if (!Tools.ContainsKey(Name) && !ActivatedLazy.ContainsKey(Name))
        {
            if (LazyDefinitions.TryGetValue(Name, out LazyToolDefinition? Def))
            {
                IAgentTool Instance = Def.FullSchemaFactory();
                AnthropicTool Schema = Def.ToStubTool(); // 실제 실행엔 스키마 불필요
                ActivatedLazy[Name] = new ToolEntry(Instance, Schema);
            }
        }

        // 활성화된 Lazy 도구 실행
        if (ActivatedLazy.TryGetValue(Name, out ToolEntry? LazyEntry))
        {
            try { return await LazyEntry.Tool.ExecuteAsync(InputJson, Session, Ct); }
            catch (Exception Ex) { return ToolResult.Error(Ex.Message); }
        }

        // 기존 Active 도구 실행
        if (!Tools.TryGetValue(Name, out ToolEntry? Entry))
            return ToolResult.Error($"Unknown tool: {Name}");

        try { return await Entry.Tool.ExecuteAsync(InputJson, Session, Ct); }
        catch (Exception Ex) { return ToolResult.Error(Ex.Message); }
    }

    /// <summary>
    /// 지정된 어셈블리에서 [AgentTool] + IAgentTool 클래스를 스캔하여 등록합니다.
    /// 인스턴스는 DI로 한 번 생성되어 재사용됩니다.
    /// </summary>
    public void DiscoverTools(params Assembly[] Assemblies)
    {
        foreach (Assembly Asm in Assemblies)
        {
            foreach (ClrType Type in Asm.GetTypes())
            {
                // [AgentTool] 어트리뷰트가 있고 IAgentTool을 구현한 클래스만 처리합니다.
                AgentToolAttribute? Attr = Type.GetCustomAttribute<AgentToolAttribute>();

                if (Attr is null)
                    continue;

                // IAgentTool을 구현한 클래스인지 체크합니다.
                if (!typeof(IAgentTool).IsAssignableFrom(Type))
                    continue;

                // DI로 인스턴스를 한 번 생성합니다.
                if (ActivatorUtilities.CreateInstance(ServiceProvider, Type) is not IAgentTool Instance)
                    continue;

                // AgentTool<TInput>에서 TInput 타입을 추출하여 스키마를 생성합니다.
                AnthropicTool Schema = new()
                {
                    Name = Attr.Name,
                    Description = Attr.Description,
                    InputSchema = GenerateSchemaFromType(Type)
                };

                Tools[Attr.Name] = new ToolEntry(Instance, Schema);
            }
        }
    }

    /// <summary>
    /// MCP 서버에서 받은 도구를 동적으로 등록합니다.
    /// 도구 이름은 "mcp__{서버이름}__{도구이름}" 형식으로 등록됩니다.
    /// </summary>
    public void RegisterMcpTools(string ServerName, McpClient Client, List<McpToolDefinition> McpTools)
    {
        foreach (McpToolDefinition Def in McpTools)
        {
            string RegistryName = $"mcp__{ServerName}__{Def.Name}";

            // MCP에서 받은 inputSchema를 그대로 Anthropic InputSchema로 변환
            InputSchema Schema = Def.InputSchema.Deserialize<InputSchema>() ?? new()
            {
                Properties = new Dictionary<string, JsonElement>(),
                Required = new List<string>()
            };

            AnthropicTool ToolSchema = new()
            {
                Name = RegistryName,
                Description = Def.Description,
                InputSchema = Schema
            };

            McpProxyTool Proxy = new(Client, Def.Name);

            Tools[RegistryName] = new ToolEntry(Proxy, ToolSchema);
            PermissionEngine.RegisterToolAnnotations(RegistryName, Def.Annotations);
        }
    }

    /// <summary>
    /// MCP 도구를 Lazy로 등록합니다.
    /// Claude에게는 스텁 스키마만 노출되고, 첫 호출 시 McpProxyTool이 생성됩니다.
    /// </summary>
    public void RegisterLazyMcpTool(McpClient Client, McpToolDefinition Def, string ServerName, string VerificationHint)
    {
        string RegistryName = $"mcp__{ServerName}__{Def.Name}";

        InputSchema FullSchema = Def.InputSchema.Deserialize<InputSchema>() ?? new()
        {
            Properties = new Dictionary<string, JsonElement>(),
            Required = new List<string>()
        };

        LazyDefinitions[RegistryName] = new LazyToolDefinition(
            Name: RegistryName,
            Description: Def.Description ?? string.Empty,
            StubSchema: FullSchema,                          // MCP 도구는 풀 스키마 = 스텁 스키마
            FullSchemaFactory: () => new McpProxyTool(Client, Def.Name),
            VerificationHint: VerificationHint
        );
        PermissionEngine.RegisterToolAnnotations(RegistryName, Def.Annotations);
    }

    /// <summary>
    /// 해당 MCP 서버의 도구가 하나라도 등록되어 있는지 확인합니다.
    /// McpReconnectService가 재시도 대상을 판별할 때 사용합니다.
    /// </summary>
    public bool HasRegisteredMcpServer(string ServerName)
    {
        string Prefix = $"mcp__{ServerName}__";
        return Tools.Keys.Any(K => K.StartsWith(Prefix, StringComparison.Ordinal))
            || LazyDefinitions.Keys.Any(K => K.StartsWith(Prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// 세션 초기화 시 활성화된 Lazy 도구를 리셋합니다.
    /// </summary>
    public void ResetLazy() => ActivatedLazy.Clear();

    /// <summary>
    /// 현재 활성화된 Lazy 도구들의 VerificationHint 목록을 반환합니다.
    /// HarnessOrchestrator가 EvaluatorAgent에 전달할 때 사용합니다.
    /// </summary>
    public IReadOnlyList<string> GetActivatedVerificationHints()
        => ActivatedLazy.Keys
            .Where(Name => LazyDefinitions.ContainsKey(Name))
            .Select(Name => LazyDefinitions[Name].VerificationHint)
            .Where(H => !string.IsNullOrEmpty(H))
            .ToList();

    /// <summary>
    /// AgentTool의 TInput 레코드에서 InputSchema를 자동 생성합니다.
    /// [Description] 어트리뷰트로 파라미터 설명을, [JsonPropertyName]으로 JSON 키를 지정합니다.
    /// </summary>
    private static InputSchema GenerateSchemaFromType(ClrType ToolType)
    {
        ClrType? InputType = FindInputType(ToolType);
        if (InputType is null)
        {
            return new InputSchema
            {
                Properties = new Dictionary<string, JsonElement>(),
                Required = new List<string>()
            };
        }

        Dictionary<string, JsonElement> Properties = new();
        List<string> Required = [];

        foreach (PropertyInfo Prop in InputType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // JSON 키: [JsonPropertyName]이 있으면 사용, 없으면 camelCase
            string JsonName = Prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                              ?? char.ToLowerInvariant(Prop.Name[0]) + Prop.Name[1..];

            string Description = Prop.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            string TypeName = GetJsonSchemaType(Prop.PropertyType);


            Dictionary<string, string> Schema = new()
            {
                ["type"] = TypeName,
                ["description"] = Description
            };

            Properties[JsonName] = JsonSerializer.SerializeToElement(Schema);

            // Nullable이 아닌 프로퍼티는 required로 등록합니다.
            if (!IsNullable(Prop))
                Required.Add(JsonName);
        }

        return new InputSchema { Properties = Properties, Required = Required };
    }

    /// <summary>AgentTool 상속 체인에서 TInput 타입을 추출합니다.</summary>
    private static ClrType? FindInputType(ClrType ToolType)
    {
        // 예: MyTool → AgentTool<MyToolInput> → object 순서로 올라감
        ClrType? Current = ToolType;

        while (Current is not null)
        {
            // 현재 타입이 AgentTool<> 이면 TInput을 꺼내서 반환
            if (Current.IsGenericType && Current.GetGenericTypeDefinition() == typeof(AgentTool<>))
                return Current.GetGenericArguments()[0];

            // 아니면 부모 클래스로 한 칸 올라감
            Current = Current.BaseType;
        }

        // 상속 체인에 AgentTool<>이 없으면 null
        return null;
    }

    /// <summary>C# 타입을 JSON Schema 타입 문자열로 변환합니다.</summary>
    private static string GetJsonSchemaType(ClrType ClrType)
    {
        ClrType Underlying = Nullable.GetUnderlyingType(ClrType) ?? ClrType;

        if (Underlying == typeof(string)) return "string";
        if (Underlying == typeof(int) || Underlying == typeof(long)) return "integer";
        if (Underlying == typeof(double) || Underlying == typeof(float) || Underlying == typeof(decimal)) return "number";
        if (Underlying == typeof(bool)) return "boolean";
        if (Underlying.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(Underlying) && Underlying != typeof(string))
            return "array";

        return "object";
    }

    /// <summary>프로퍼티가 nullable인지 확인합니다. 참조 타입도 string vs string? 구분 가능.</summary>
    private static bool IsNullable(PropertyInfo Prop)
    {
        // 값 타입: int? 등은 Nullable<T>로 감싸져 있음
        if (Prop.PropertyType.IsValueType)
            return Nullable.GetUnderlyingType(Prop.PropertyType) is not null;

        // 참조 타입: NullabilityInfoContext로 string vs string? 구분
        NullabilityInfoContext Context = new();
        NullabilityInfo Info = Context.Create(Prop);
        return Info.WriteState == NullabilityState.Nullable;
    }
}
