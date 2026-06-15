using UnrealAgent.Backend.Agent.Middleware;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Mode;
using UnrealAgent.Backend.Security;
using UnrealAgent.Backend.Team;

namespace UnrealAgent.Backend.Agent;

public sealed class AgentSession
{
    public Conversation.Conversation Conversation { get; } = new();
    public Team.Team Team { get; } = new();
    public PermissionEngine PermissionEngine { get; } = new();
    public AgentMode Mode { get; set; } = AgentMode.Normal;

    private readonly AgentPipeline Pipeline;

    public AgentSession(AgentLoop Loop, SlashCommandMiddleware SlashCommandMiddleware)
    {
        Pipeline = new AgentPipeline()
            .Use(SlashCommandMiddleware)
            .Run(Loop.RunAsync);
    }

    public IAsyncEnumerable<ChatEvent> ProcessMessage(UserInput Input, CancellationToken Ct = default)
        => Pipeline.RunAsync(Input, this, Ct);
}
