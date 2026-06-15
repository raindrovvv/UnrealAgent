using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Agent.Harness;
using UnrealAgent.Backend.Agent.Middleware;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Claude;
using UnrealAgent.Backend.Provider;
using UnrealAgent.Backend.Codex;
using UnrealAgent.Backend.Command;
using UnrealAgent.Backend.Command.Commands;
using UnrealAgent.Backend.Mcp;
using UnrealAgent.Backend.Model;
using UnrealAgent.Backend.Model.Models;
using UnrealAgent.Backend.Prompt;
using UnrealAgent.Backend.Experience;
using UnrealAgent.Backend.Recovery;
using UnrealAgent.Backend.Skill;
using UnrealAgent.Backend.Tool;
using UnrealAgent.Backend.Tool.Tools;
using UnrealAgent.Frontend.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;

static string? GetArgValue(string[] args, string key)
{
    foreach (string arg in args)
    {
        if (!arg.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            continue;

        int separatorIndex = arg.IndexOf('=');
        if (separatorIndex < 0 || separatorIndex == arg.Length - 1)
            continue;

        return arg[(separatorIndex + 1)..].Trim().Trim('"');
    }

    return null;
}

// ── WebApplicationBuilder (서비스 등록 + 앱 설정을 담는 빌더) 생성 ──
// 이 서버는 항상 Unreal Editor 내부에서 로컬 실행되므로 Development 환경으로 고정합니다.
// Production 모드에서는 StaticWebAssetsLoader가 매니페스트를 건너뛰어 collocated .razor.js가
// 404를 반환하고, JsComponentBase의 JS import가 JSException을 던져 Blazor 회선이 끊깁니다.
WebApplicationBuilder Builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = Environments.Development,
});

string FrontendUrl = GetArgValue(args, "--frontend-url") ?? "http://127.0.0.1:55558";
string? OverrideMcpUrl = GetArgValue(args, "--mcp-url");

// 에디터에서 숨김 프로세스로 실행되므로 Windows Event Log 로거는 제거합니다.
Builder.Logging.ClearProviders();
Builder.Logging.AddConsole();

// ── Kestrel (요청을 받아서 넘겨주는 서버 엔진) 포트 설정 ──
Builder.WebHost.UseUrls(FrontendUrl);

// ── Blazor 서비스 등록 (Razor 컴포넌트 + 서버 측 인터랙티브 모드) ──
Builder.Services.AddRazorComponents().AddInteractiveServerComponents();
Builder.Services.Configure<HubOptions>(Options =>
{
    Options.MaximumReceiveMessageSize = 16 * 1024 * 1024;
});

// ── DataProtection 키를 프로젝트 로컬 writable 경로에 저장합니다 ──
string DataProtectionDir = Path.Combine(AppContext.BaseDirectory, ".unrealagent", "data-protection");
Directory.CreateDirectory(DataProtectionDir);
Builder.Services.AddDataProtection()
    .SetApplicationName("UnrealAgent.Frontend")
    .PersistKeysToFileSystem(new DirectoryInfo(DataProtectionDir));

// ── HTTP 클라이언트 등록 (외부 API 호출용) ──
Builder.Services.AddHttpClient("WebFetch");
Builder.Services.AddHttpClient("OpenAICompat");

// ── Auth 모듈 ──
Builder.Services.AddSingleton<AuthConfig>();
Builder.Services.AddSingleton<CodexCliService>();
Builder.Services.AddSingleton<ClaudeCliService>();

// ── Model Providers ──
Builder.Services.AddSingleton<IModelProvider, AnthropicProvider>();
Builder.Services.AddSingleton<IModelProvider, ClaudeCliProvider>();
Builder.Services.AddSingleton<IModelProvider, OpenAICompatProvider>();
Builder.Services.AddSingleton<IModelProvider, CodexCliProvider>();
Builder.Services.AddSingleton<ProviderFactory>();

// ── Skill 모듈 ──
Builder.Services.AddSingleton<SkillRegistry>();

// ── Command 모듈 ──
Builder.Services.AddSingleton<CommandRegistry>();

// ── Agent 미들웨어 파이프라인 ──
Builder.Services.AddSingleton<SlashCommandMiddleware>();

// ── Agent 모듈 (에이전트 루프 + 세션) ──
Builder.Services.AddSingleton<AgentSession>();
Builder.Services.AddSingleton<AgentLoop>();

// ── Harness 모듈 (Planner → Generator → Evaluator) ──
Builder.Services.AddSingleton<PlannerAgent>();
Builder.Services.AddSingleton<EvaluatorAgent>();
Builder.Services.AddSingleton<SubtaskRunner>();
Builder.Services.AddSingleton<HarnessOrchestrator>();

// ── AgentRunner (메시지 큐 + 에이전트 루프 서비스) ──
Builder.Services.AddSingleton<AgentRunner>();
Builder.Services.AddHostedService(Sp => Sp.GetRequiredService<AgentRunner>());

// ── MCP 재연결 서비스 (에디터가 늦게 뜬 경우 백그라운드 재시도) ──
Builder.Services.AddSingleton(new McpReconnectOptions { OverrideMcpUrl = OverrideMcpUrl });
Builder.Services.AddSingleton(new McpHealthService(OverrideMcpUrl));
Builder.Services.AddHostedService<McpReconnectService>();

// ── Runtime 모듈 ──
Builder.Services.AddSingleton<DocsRagService>();
Builder.Services.AddSingleton<PromptBuilder>();

// ── Recovery 모듈 ──
Builder.Services.AddSingleton<RecoveryService>();

// ── Experience 모듈 ──
Builder.Services.AddSingleton<ExperienceCapturer>();

// ── Tool 모듈 ──
Builder.Services.AddSingleton<ToolRegistry>();
Builder.Services.AddSingleton<ToolExecutor>();

// ── Claude 모델 레지스트리 & 런타임 설정 ──
Builder.Services.AddSingleton<ModelRegistry>();
Builder.Services.AddSingleton<ModelSettings>();

// 여기까지 서비스 등록 단계. Build() 이후는 미들웨어/라우팅 설정 단계입니다.
WebApplication App = Builder.Build();

// ── 어트리뷰트 기반 자동 스캔 ──
App.Services.GetRequiredService<ToolRegistry>().DiscoverTools(typeof(WebSearch).Assembly);
App.Services.GetRequiredService<ModelRegistry>().DiscoverModels(typeof(Opus46).Assembly);
App.Services.GetRequiredService<CommandRegistry>().DiscoverCommands(typeof(ClearCommand).Assembly);
App.Services.GetRequiredService<SkillRegistry>().DiscoverSkills();

// ── 설정 로드 ──
App.Services.GetRequiredService<AuthConfig>().Load();
App.Services.GetRequiredService<ModelSettings>().Load();

// ── MCP 서버 연결 + 도구 등록 (첫 시도) ──
// 에디터가 아직 준비되지 않아 실패한 서버는 McpReconnectService가 백그라운드에서 재시도합니다.
{
    ToolRegistry Registry = App.Services.GetRequiredService<ToolRegistry>();
    RecoveryService RecoverySvc = App.Services.GetRequiredService<RecoveryService>();

    Dictionary<string, McpServerConfig> McpServers = McpConfig.Load();
    if (!string.IsNullOrWhiteSpace(OverrideMcpUrl))
        McpServers["UnrealMCP"] = new McpServerConfig(OverrideMcpUrl);

    foreach ((string Name, McpServerConfig Config) in McpServers)
    {
        try
        {
            await McpRegistrar.RegisterServerAsync(
                Name, Config, Registry, RecoverySvc,
                TimeSpan.FromSeconds(30));
        }
        catch (Exception Ex)
        {
            Console.WriteLine($"[MCP] {Name} 초기 연결 실패 (백그라운드 재시도 예약): {Ex.Message}");
        }
    }
}

// ── 미들웨어 파이프라인 ──
App.UseStaticFiles();
App.UseAntiforgery();

// ── Blazor 엔드포인트 (Razor 컴포넌트 라우팅 + 서버 렌더 모드 적용) ──
App.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// ── 서버 실행 (요청 수신 대기 시작) ──
App.Run();
