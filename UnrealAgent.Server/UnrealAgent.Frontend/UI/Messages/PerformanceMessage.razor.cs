using Microsoft.AspNetCore.Components;
using UnrealAgent.Backend.Chat;

namespace UnrealAgent.Frontend.UI.Messages;

public partial class PerformanceMessage
{
    [Parameter] public ChatUIMessage.Performance Message { get; set; } = null!;
}
