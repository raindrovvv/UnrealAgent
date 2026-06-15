#include "McpServer.h"
#include "HttpServerModule.h"
#include "IHttpRouter.h"
#include "HttpServerRequest.h"
#include "HttpServerResponse.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/DateTime.h"
#include "Interfaces/IPluginManager.h"
#include "Sockets.h"
#include "SocketSubsystem.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(McpServer)

DEFINE_LOG_CATEGORY_STATIC(McpServerLog, Log, All);

namespace
{
FString QuoteProcessArgument(const FString& Argument)
{
	FString Escaped = Argument;
	Escaped.ReplaceInline(TEXT("\""), TEXT("\\\""));
	return FString::Printf(TEXT("\"%s\""), *Escaped);
}

FString ResolveDotnetExecutable()
{
#if PLATFORM_WINDOWS
	const FString ProgramFiles = FPlatformMisc::GetEnvironmentVariable(TEXT("ProgramFiles"));
	if (!ProgramFiles.IsEmpty())
	{
		const FString DotnetPath = FPaths::Combine(ProgramFiles, TEXT("dotnet"), TEXT("dotnet.exe"));
		if (FPaths::FileExists(DotnetPath))
		{
			return DotnetPath;
		}
	}
#endif

	return TEXT("dotnet");
}

bool IsTcpPortAvailable(uint32 Port)
{
	ISocketSubsystem* SocketSubsystem = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM);
	if (!SocketSubsystem)
	{
		return true;
	}

	TSharedRef<FInternetAddr> Addr = SocketSubsystem->CreateInternetAddr();
	bool bIsValid = false;
	Addr->SetIp(TEXT("127.0.0.1"), bIsValid);
	Addr->SetPort(static_cast<int32>(Port));
	if (!bIsValid)
	{
		return true;
	}

	FSocket* Socket = SocketSubsystem->CreateSocket(NAME_Stream, TEXT("UnrealAgentPortProbe"), false);
	if (!Socket)
	{
		return true;
	}

	Socket->SetReuseAddr(false);
	const bool bCanBind = Socket->Bind(*Addr);
	Socket->Close();
	SocketSubsystem->DestroySocket(Socket);

	return bCanBind;
}

FString GetFrontendLaunchMode()
{
	const FString EnvValue = FPlatformMisc::GetEnvironmentVariable(TEXT("UNREALAGENT_FRONTEND_LAUNCH_MODE")).TrimStartAndEnd().ToLower();
	if (EnvValue == TEXT("release") || EnvValue == TEXT("dll") || EnvValue == TEXT("debug"))
	{
		return EnvValue;
	}

	return TEXT("dll");
}

FString BuildFrontendLaunchParams(const FString& FrontendProjectPath, const FString& FrontendUrl, const FString& McpUrl)
{
	const FString Mode = GetFrontendLaunchMode();
	if (Mode == TEXT("dll"))
	{
		const FString ProjectDir = FPaths::GetPath(FrontendProjectPath);
		const FString DllPath = FPaths::Combine(ProjectDir, TEXT("bin"), TEXT("Release"), TEXT("net10.0"), TEXT("UnrealAgent.Frontend.dll"));
		if (FPaths::FileExists(DllPath))
		{
			return FString::Printf(
				TEXT("%s --frontend-url=%s --mcp-url=%s"),
				*QuoteProcessArgument(DllPath),
				*QuoteProcessArgument(FrontendUrl),
				*QuoteProcessArgument(McpUrl));
		}

		UE_LOG(McpServerLog, Warning, TEXT("UNREALAGENT_FRONTEND_LAUNCH_MODE=dll 이지만 Release DLL이 없어 Debug dotnet run으로 대체합니다: %s"), *DllPath);
	}

	const TCHAR* Configuration = Mode == TEXT("release") ? TEXT("Release") : TEXT("Debug");
	return FString::Printf(
		TEXT("run --project %s --configuration %s --no-launch-profile -- --frontend-url=%s --mcp-url=%s"),
		*QuoteProcessArgument(FrontendProjectPath),
		Configuration,
		*QuoteProcessArgument(FrontendUrl),
		*QuoteProcessArgument(McpUrl));
}

bool IsReadOnlyToolName(const FString& ToolName)
{
	return ToolName.StartsWith(TEXT("get_")) ||
		ToolName.StartsWith(TEXT("list_")) ||
		ToolName.StartsWith(TEXT("find_")) ||
		ToolName.StartsWith(TEXT("query_")) ||
		ToolName.StartsWith(TEXT("show_")) ||
		ToolName.StartsWith(TEXT("search_")) ||
		ToolName.StartsWith(TEXT("fetch_")) ||
		ToolName == TEXT("asset_search") ||
		ToolName == TEXT("capture_viewport");
}

bool IsDestructiveToolName(const FString& ToolName)
{
	return ToolName.Contains(TEXT("write")) ||
		ToolName.Contains(TEXT("edit")) ||
		ToolName.Contains(TEXT("execute")) ||
		ToolName.Contains(TEXT("compile")) ||
		ToolName.Contains(TEXT("delete")) ||
		ToolName.Contains(TEXT("create")) ||
		ToolName.Contains(TEXT("ops"));
}
}

//-----------------------------------------------------------------------------
// UEditorSubsystem 오버라이드
//-----------------------------------------------------------------------------

void UMcpServer::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);

	InitializePorts();
	DiscoverTools();
	StartHttpServer();
	StartServer();
}

void UMcpServer::Deinitialize()
{
	StopServer();
	StopHttpServer();

	Super::Deinitialize();
}

//-----------------------------------------------------------------------------
// Agent Server 프로세스
//-----------------------------------------------------------------------------

void UMcpServer::KillExistingAgentServer() const
{
	// 현재 에디터 인스턴스가 띄운 프로세스는 StopServer에서 핸들 기반으로 정리합니다.
	// 이름 기반 taskkill은 다른 프로젝트/다른 에디터 인스턴스까지 종료시킬 수 있어 사용하지 않습니다.
}

void UMcpServer::InitializePorts()
{
	// 고정 포트 사용 — Claude Code MCP 프록시가 재시작 없이 항상 연결 가능.
	FrontendPort = 55558;
	ServerPort   = 55559;
}

FString UMcpServer::GetFrontendUrl() const
{
	return FString::Printf(TEXT("http://127.0.0.1:%u"), FrontendPort);
}

FString UMcpServer::GetMcpUrl() const
{
	return FString::Printf(TEXT("http://127.0.0.1:%u/mcp"), ServerPort);
}

void UMcpServer::RestartServer()
{
	StopServer();
	StartServer();
}

bool UMcpServer::EnsureServerRunning()
{
	if (AgentServerProcess.IsValid() && FPlatformProcess::IsProcRunning(AgentServerProcess))
	{
		return true;
	}

	if (AgentServerProcess.IsValid())
	{
		int32 ReturnCode = 0;
		if (FPlatformProcess::GetProcReturnCode(AgentServerProcess, &ReturnCode))
		{
			UE_LOG(McpServerLog, Warning, TEXT("Agent Server 종료 코드: %d"), ReturnCode);
		}

		FPlatformProcess::CloseProc(AgentServerProcess);
		AgentServerProcessId = 0;
	}

	UE_LOG(McpServerLog, Warning, TEXT("Agent Server가 실행 중이 아니어서 재시작합니다."));
	StartServer();
	return AgentServerProcess.IsValid() && FPlatformProcess::IsProcRunning(AgentServerProcess);
}

void UMcpServer::StartServer()
{
	if (IsRunningCommandlet())
	{
		return;
	}

	if (AgentServerProcess.IsValid() && FPlatformProcess::IsProcRunning(AgentServerProcess))
	{
		return;
	}

	if (AgentServerProcess.IsValid())
	{
		FPlatformProcess::CloseProc(AgentServerProcess);
		AgentServerProcess.Reset();
		AgentServerProcessId = 0;
	}

	if (!ValidatePortAvailability(FrontendPort, TEXT("Frontend")))
	{
		return;
	}

	const TSharedPtr<IPlugin> Plugin = IPluginManager::Get().FindPlugin(TEXT("UnrealAgent"));
	if (!Plugin.IsValid())
	{
		UE_LOG(McpServerLog, Error, TEXT("UnrealAgent 플러그인 경로를 찾을 수 없습니다."));
		return;
	}

	const FString FrontendProjectPath = FPaths::ConvertRelativePathToFull(FPaths::Combine(
		Plugin->GetBaseDir(),
		TEXT("UnrealAgent.Server"),
		TEXT("UnrealAgent.Frontend"),
		TEXT("UnrealAgent.Frontend.csproj")));

	if (!FPaths::FileExists(FrontendProjectPath))
	{
		UE_LOG(McpServerLog, Error, TEXT("UnrealAgent Frontend 프로젝트 파일을 찾을 수 없습니다: %s"), *FrontendProjectPath);
		return;
	}

	const FString WorkingDirectory = FPaths::GetPath(FrontendProjectPath);
	const FString Params = BuildFrontendLaunchParams(FrontendProjectPath, GetFrontendUrl(), GetMcpUrl());
	const FString DotnetExecutable = ResolveDotnetExecutable();

	UE_LOG(McpServerLog, Log, TEXT("UnrealAgent Frontend를 시작합니다: %s %s"), *DotnetExecutable, *Params);

	AgentServerProcess = FPlatformProcess::CreateProc(
		*DotnetExecutable,
		*Params,
		false,
		true,
		true,
		&AgentServerProcessId,
		0,
		*WorkingDirectory,
		nullptr,
		nullptr);

	if (!AgentServerProcess.IsValid())
	{
		AgentServerProcessId = 0;
		UE_LOG(McpServerLog, Error, TEXT("dotnet 프로세스를 시작하지 못했습니다. .NET SDK 설치와 PATH를 확인하세요."));
		return;
	}

	UE_LOG(McpServerLog, Log, TEXT("UnrealAgent Frontend 시작됨 (PID: %d, URL: %s)"), AgentServerProcessId, *GetFrontendUrl());
}

void UMcpServer::StopServer()
{
	if (!AgentServerProcess.IsValid())
	{
		return;
	}

	if (FPlatformProcess::IsProcRunning(AgentServerProcess))
	{
		FPlatformProcess::TerminateProc(AgentServerProcess, true);
		UE_LOG(McpServerLog, Log, TEXT("Agent Server를 종료했습니다 (PID: %d)"), AgentServerProcessId);
	}

	FPlatformProcess::CloseProc(AgentServerProcess);
	AgentServerProcess.Reset();
	AgentServerProcessId = 0;
}

//-----------------------------------------------------------------------------
// HTTP 서버
//-----------------------------------------------------------------------------

void UMcpServer::StartHttpServer()
{
	if (IsRunningCommandlet())
	{
		return;
	}

	FHttpServerModule& HttpServerModule = FHttpServerModule::Get();
	if (!ValidatePortAvailability(ServerPort, TEXT("MCP")))
	{
		return;
	}

	HttpRouter = HttpServerModule.GetHttpRouter(ServerPort);

	if (!HttpRouter.IsValid())
	{
		UE_LOG(McpServerLog, Error, TEXT("HTTP 라우터를 생성할 수 없습니다 (포트: %d)"), ServerPort);

		return;
	}

	McpRouteHandle = HttpRouter->BindRoute(
		FHttpPath(TEXT("/mcp")),
		EHttpServerRequestVerbs::VERB_POST,
		FHttpRequestHandler::CreateUObject(this, &ThisClass::HandleMcpRequest));

	HttpServerModule.StartAllListeners();
	SyncProjectMcpConfig();
}

void UMcpServer::StopHttpServer()
{
	if (HttpRouter.IsValid())
	{
		HttpRouter->UnbindRoute(McpRouteHandle);
		HttpRouter.Reset();
	}
}

void UMcpServer::SyncProjectMcpConfig() const
{
	const FString ConfigDir = FPaths::Combine(FPaths::ProjectDir(), TEXT(".unrealagent"));
	const FString SettingsPath = FPaths::Combine(ConfigDir, TEXT("settings.local.json"));

	IFileManager::Get().MakeDirectory(*ConfigDir, true);

	TSharedPtr<FJsonObject> Root = MakeShared<FJsonObject>();

	FString ExistingJson;
	if (FFileHelper::LoadFileToString(ExistingJson, *SettingsPath))
	{
		TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(ExistingJson);
		if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid())
			Root = MakeShared<FJsonObject>();
	}

	const TSharedPtr<FJsonObject>* ExistingMcpServers = nullptr;
	TSharedPtr<FJsonObject> McpServers = Root->TryGetObjectField(TEXT("mcpServers"), ExistingMcpServers)
		? *ExistingMcpServers
		: MakeShared<FJsonObject>();

	const TSharedPtr<FJsonObject>* ExistingUnrealMcp = nullptr;
	TSharedPtr<FJsonObject> UnrealMcp = McpServers->TryGetObjectField(TEXT("UnrealMCP"), ExistingUnrealMcp)
		? *ExistingUnrealMcp
		: MakeShared<FJsonObject>();

	UnrealMcp->SetStringField(TEXT("url"), GetMcpUrl());
	McpServers->SetObjectField(TEXT("UnrealMCP"), UnrealMcp);
	Root->SetObjectField(TEXT("mcpServers"), McpServers);
	Root->SetNumberField(TEXT("owner_pid"), static_cast<double>(FPlatformProcess::GetCurrentProcessId()));
	Root->SetStringField(TEXT("project_path"), FPaths::ConvertRelativePathToFull(FPaths::ProjectDir()));
	Root->SetStringField(TEXT("frontend_url"), GetFrontendUrl());
	Root->SetStringField(TEXT("mcp_url"), GetMcpUrl());
	Root->SetStringField(TEXT("updated_at_utc"), FDateTime::UtcNow().ToIso8601());

	FString Output;
	TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Output);
	FJsonSerializer::Serialize(Root.ToSharedRef(), Writer);

	if (!FFileHelper::SaveStringToFile(Output, *SettingsPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
	{
		UE_LOG(McpServerLog, Warning, TEXT("settings.local.json 저장에 실패했습니다: %s"), *SettingsPath);
		return;
	}

	UE_LOG(McpServerLog, Log, TEXT("프로젝트 MCP 설정을 동기화했습니다: %s"), *GetMcpUrl());
}

bool UMcpServer::ValidatePortAvailability(uint32 Port, const TCHAR* PortName) const
{
	if (IsTcpPortAvailable(Port))
	{
		return true;
	}

	const FString SettingsPath = FPaths::Combine(FPaths::ProjectDir(), TEXT(".unrealagent"), TEXT("settings.local.json"));
	TSharedPtr<FJsonObject> Root;
	FString ExistingJson;
	if (FFileHelper::LoadFileToString(ExistingJson, *SettingsPath))
	{
		TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(ExistingJson);
		FJsonSerializer::Deserialize(Reader, Root);
	}

	UE_LOG(McpServerLog, Error, TEXT("UnrealAgent %s 포트 %u가 이미 사용 중입니다. 다른 에디터/프로젝트 또는 남은 dotnet 프로세스를 종료한 뒤 다시 시도하세요."), PortName, Port);
	LogExistingOwnerHint(Root);

	return false;
}

void UMcpServer::LogExistingOwnerHint(const TSharedPtr<FJsonObject>& Root) const
{
	if (!Root.IsValid())
	{
		return;
	}

	FString ProjectPath;
	FString FrontendUrl;
	FString McpUrl;
	FString UpdatedAt;
	int32 OwnerPid = 0;
	Root->TryGetStringField(TEXT("project_path"), ProjectPath);
	Root->TryGetStringField(TEXT("frontend_url"), FrontendUrl);
	Root->TryGetStringField(TEXT("mcp_url"), McpUrl);
	Root->TryGetStringField(TEXT("updated_at_utc"), UpdatedAt);
	Root->TryGetNumberField(TEXT("owner_pid"), OwnerPid);

	if (!ProjectPath.IsEmpty() || OwnerPid != 0)
	{
		UE_LOG(McpServerLog, Warning, TEXT("이전 UnrealAgent owner 힌트: PID=%d Project=%s Frontend=%s MCP=%s Updated=%s"),
			OwnerPid, *ProjectPath, *FrontendUrl, *McpUrl, *UpdatedAt);
	}
}

//-----------------------------------------------------------------------------
// JSON-RPC 2.0 라우팅
//-----------------------------------------------------------------------------

bool UMcpServer::HandleMcpRequest(const FHttpServerRequest& Request, const FHttpResultCallback& OnComplete)
{
	static constexpr int32 MaxRequestBodyBytes = 4 * 1024 * 1024;
	const TSharedPtr<FJsonValue> NullId = MakeShared<FJsonValueNull>();

	if (Request.Body.Num() > MaxRequestBodyBytes)
	{
		const FString ErrorJson = SerializeJson(MakeJsonRpcError(NullId, -32600, TEXT("Invalid Request: body too large")));
		OnComplete(FHttpServerResponse::Create(ErrorJson, TEXT("application/json")));
		return true;
	}

	// 요청 본문을 JSON으로 파싱합니다
	const FUTF8ToTCHAR BodyConverter(reinterpret_cast<const char*>(Request.Body.GetData()), Request.Body.Num());
	const FString BodyString(BodyConverter.Length(), BodyConverter.Get());

	TSharedPtr<FJsonObject> JsonObject;
	const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(BodyString);

	if (!FJsonSerializer::Deserialize(Reader, JsonObject) || !JsonObject.IsValid())
	{
		const FString ErrorJson = SerializeJson(MakeJsonRpcError(NullId, -32700, TEXT("Parse error")));
		OnComplete(FHttpServerResponse::Create(ErrorJson, TEXT("application/json")));

		return true;
	}

	FString JsonRpcVersion;
	if (!JsonObject->TryGetStringField(TEXT("jsonrpc"), JsonRpcVersion) || JsonRpcVersion != TEXT("2.0"))
	{
		const FString ErrorJson = SerializeJson(MakeJsonRpcError(NullId, -32600, TEXT("Invalid Request: jsonrpc must be \"2.0\"")));
		OnComplete(FHttpServerResponse::Create(ErrorJson, TEXT("application/json")));
		return true;
	}

	FString Method;
	if (!JsonObject->TryGetStringField(TEXT("method"), Method) || Method.IsEmpty())
	{
		const FString ErrorJson = SerializeJson(MakeJsonRpcError(NullId, -32600, TEXT("Invalid Request: missing method")));
		OnComplete(FHttpServerResponse::Create(ErrorJson, TEXT("application/json")));
		return true;
	}

	const TSharedPtr<FJsonValue> RequestId = JsonObject->TryGetField(TEXT("id"));
	if (!RequestId.IsValid())
	{
		if (Method.StartsWith(TEXT("notifications/")))
		{
			// Some MCP HTTP clients validate the response content type even for
			// JSON-RPC notifications. Keep the body minimal, but never return the
			// default text/plain response because Codex treats that as transport fatal.
			OnComplete(FHttpServerResponse::Create(TEXT("{}"), TEXT("application/json")));
			return true;
		}

		const FString ErrorJson = SerializeJson(MakeJsonRpcError(NullId, -32600, TEXT("Invalid Request: id is required for requests")));
		OnComplete(FHttpServerResponse::Create(ErrorJson, TEXT("application/json")));
		return true;
	}

	if (RequestId->Type != EJson::Number && RequestId->Type != EJson::String && RequestId->Type != EJson::Null)
	{
		const FString ErrorJson = SerializeJson(MakeJsonRpcError(NullId, -32600, TEXT("Invalid Request: id must be string, number, or null")));
		OnComplete(FHttpServerResponse::Create(ErrorJson, TEXT("application/json")));
		return true;
	}

	// 메서드별로 라우팅합니다
	TSharedPtr<FJsonObject> Response;

	if (Method == TEXT("initialize"))
	{
		Response = HandleInitialize(RequestId);
	}
	else if (Method == TEXT("tools/list"))
	{
		Response = HandleToolsList(RequestId);
	}
	else if (Method == TEXT("tools/call"))
	{
		const TSharedPtr<FJsonObject>* Params = nullptr;
		if (JsonObject->HasField(TEXT("params")) && !JsonObject->TryGetObjectField(TEXT("params"), Params))
		{
			Response = MakeJsonRpcError(RequestId, -32602, TEXT("Invalid params: expected object"));
		}
		else
		{
			Response = HandleToolsCall(RequestId, Params ? *Params : nullptr);
		}
	}
	else
	{
		Response = MakeJsonRpcError(RequestId, -32601, FString::Printf(TEXT("Method not found: %s"), *Method));
	}

	OnComplete(FHttpServerResponse::Create(SerializeJson(Response), TEXT("application/json")));

	return true;
}

TSharedPtr<FJsonObject> UMcpServer::HandleInitialize(const TSharedPtr<FJsonValue>& RequestId) const
{
	// 서버 정보를 구성합니다
	const TSharedPtr<FJsonObject> ServerInfo = MakeShared<FJsonObject>();
	ServerInfo->SetStringField(TEXT("name"), TEXT("unreal-agent"));
	ServerInfo->SetStringField(TEXT("version"), TEXT("1.0.0"));

	// capabilities: 도구 제공
	const TSharedPtr<FJsonObject> Tools = MakeShared<FJsonObject>();
	const TSharedPtr<FJsonObject> Capabilities = MakeShared<FJsonObject>();
	Capabilities->SetObjectField(TEXT("tools"), Tools);

	// 미완성 태스크 복구 힌트
	TSharedPtr<FJsonObject> RecoveryHint = LoadRecoveryHint();
	if (RecoveryHint.IsValid())
		Capabilities->SetObjectField(TEXT("recovery_hint"), RecoveryHint);

	// result 조립
	const TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetStringField(TEXT("protocolVersion"), TEXT("2025-03-26"));
	Result->SetObjectField(TEXT("serverInfo"), ServerInfo);
	Result->SetObjectField(TEXT("capabilities"), Capabilities);

	return MakeJsonRpcResponse(RequestId, Result);
}

TSharedPtr<FJsonObject> UMcpServer::HandleToolsList(const TSharedPtr<FJsonValue>& RequestId) const
{
	// 사전 빌드된 도구 정의 목록을 반환합니다
	const TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetArrayField(TEXT("tools"), ToolDefinitions);

	return MakeJsonRpcResponse(RequestId, Result);
}

TSharedPtr<FJsonObject> UMcpServer::HandleToolsCall(const TSharedPtr<FJsonValue>& RequestId, const TSharedPtr<FJsonObject>& Params)
{
	if (!Params.IsValid())
	{
		return MakeJsonRpcError(RequestId, -32602, TEXT("Missing params"));
	}

	FString ToolName;
	if (!Params->TryGetStringField(TEXT("name"), ToolName) || ToolName.IsEmpty())
	{
		return MakeJsonRpcError(RequestId, -32602, TEXT("Missing tool name"));
	}

	// arguments 추출 (없으면 빈 객체)
	const TSharedPtr<FJsonObject>* ArgumentsPtr = nullptr;
	if (Params->HasField(TEXT("arguments")) && !Params->TryGetObjectField(TEXT("arguments"), ArgumentsPtr))
	{
		return MakeJsonRpcError(RequestId, -32602, TEXT("Invalid arguments: expected object"));
	}
	const TSharedPtr<FJsonObject> Arguments = ArgumentsPtr ? *ArgumentsPtr : MakeShared<FJsonObject>();

	// 도구 실행
	FString InvalidParamsError;
	const FMcpResponse ToolResponse = ExecuteTool(ToolName, Arguments, &InvalidParamsError);
	if (!InvalidParamsError.IsEmpty())
	{
		return MakeJsonRpcError(RequestId, -32602, InvalidParamsError);
	}

	// MCP ToolCallResult 형식으로 변환합니다
	// { "content": [{"type":"text","text":"..."}], "isError": bool }
	const TSharedPtr<FJsonObject> ContentItem = MakeShared<FJsonObject>();
	ContentItem->SetStringField(TEXT("type"), TEXT("text"));
	ContentItem->SetStringField(TEXT("text"), ToolResponse.GetText());

	TArray<TSharedPtr<FJsonValue>> ContentArray;
	ContentArray.Add(MakeShared<FJsonValueObject>(ContentItem));

	const TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetArrayField(TEXT("content"), ContentArray);
	Result->SetBoolField(TEXT("isError"), !ToolResponse.bSuccess);

	return MakeJsonRpcResponse(RequestId, Result);
}

//-----------------------------------------------------------------------------
// 도구 관리
//-----------------------------------------------------------------------------

void UMcpServer::DiscoverTools()
{
	const UScriptStruct* BaseStruct = FMcpTool::StaticStruct();

	for (TObjectIterator<UScriptStruct> It; It; ++It)
	{
		UScriptStruct* Struct = *It;

		// Hidden 메타가 있는 구조체는 제외합니다
		if (Struct->HasMetaData(TEXT("Hidden")))
		{
			continue;
		}

		// FMcpTool을 상속하지 않으면 제외합니다
		if (!Struct->IsChildOf(BaseStruct))
		{
			continue;
		}

		// McpTool 메타에서 도구 이름을 읽습니다
		const FString ToolName = Struct->GetMetaData(TEXT("McpTool"));
		if (ToolName.IsEmpty())
		{
			continue;
		}

		if (ToolMap.Contains(ToolName))
		{
			UE_LOG(McpServerLog, Error, TEXT("중복된 도구 이름: %s (%s, %s)"),
				*ToolName, *ToolMap[ToolName]->GetName(), *Struct->GetName());

			continue;
		}

		ToolMap.Add(ToolName, Struct);

		// MCP 도구 정의를 생성합니다 (tools/list 응답용)
		const TSharedPtr<FJsonObject> ToolDef = MakeShared<FJsonObject>();
		ToolDef->SetStringField(TEXT("name"), ToolName);

		// 임시 인스턴스를 생성하여 ToolDescription()을 호출합니다
		{
			uint8* StructMemory = static_cast<uint8*>(FMemory::Malloc(Struct->GetStructureSize()));
			Struct->InitializeStruct(StructMemory);

			const FMcpTool* ToolInstance = reinterpret_cast<const FMcpTool*>(StructMemory);
			const FString Description = ToolInstance->ToolDescription();

			Struct->DestroyStruct(StructMemory);
			FMemory::Free(StructMemory);

			if (!Description.IsEmpty())
			{
				ToolDef->SetStringField(TEXT("description"), Description);
			}
		}

		ToolDef->SetObjectField(TEXT("inputSchema"), BuildInputSchema(Struct));
		const TSharedPtr<FJsonObject> Annotations = MakeShared<FJsonObject>();
		Annotations->SetBoolField(TEXT("readOnlyHint"), IsReadOnlyToolName(ToolName));
		Annotations->SetBoolField(TEXT("destructiveHint"), IsDestructiveToolName(ToolName));
		Annotations->SetBoolField(TEXT("requiresApproval"), !IsReadOnlyToolName(ToolName));
		ToolDef->SetObjectField(TEXT("annotations"), Annotations);
		ToolDefinitions.Add(MakeShared<FJsonValueObject>(ToolDef));
	}
}

TSharedPtr<FJsonObject> UMcpServer::BuildInputSchema(const UScriptStruct* Struct) const
{
	const TSharedPtr<FJsonObject> Schema = MakeShared<FJsonObject>();
	Schema->SetStringField(TEXT("type"), TEXT("object"));

	const TSharedPtr<FJsonObject> Properties = MakeShared<FJsonObject>();
	TArray<TSharedPtr<FJsonValue>> Required;

	for (TFieldIterator<FProperty> It(Struct); It; ++It)
	{
		const FProperty* Property = *It;

		if (!Property->HasMetaData(TEXT("ToolParam")))
		{
			continue;
		}

		const FString JsonKey = GetParamJsonKey(Property);

		// UPROPERTY 타입 → JSON Schema 타입
		const TSharedPtr<FJsonObject> PropSchema = MakeShared<FJsonObject>();

		if (CastField<FStrProperty>(Property))
		{
			PropSchema->SetStringField(TEXT("type"), TEXT("string"));
		}
		else if (CastField<FIntProperty>(Property) || CastField<FInt64Property>(Property))
		{
			PropSchema->SetStringField(TEXT("type"), TEXT("integer"));
		}
		else if (CastField<FFloatProperty>(Property) || CastField<FDoubleProperty>(Property))
		{
			PropSchema->SetStringField(TEXT("type"), TEXT("number"));
		}
		else if (CastField<FBoolProperty>(Property))
		{
			PropSchema->SetStringField(TEXT("type"), TEXT("boolean"));
		}
		else
		{
			// 미지원 타입은 any로 처리합니다
			UE_LOG(McpServerLog, Warning, TEXT("미지원 ToolParam 타입: %s.%s (%s)"),
				*Struct->GetName(), *Property->GetName(), *Property->GetCPPType());
		}

		// Description 메타 → JSON Schema description
		if (Property->HasMetaData(TEXT("Description")))
		{
			PropSchema->SetStringField(TEXT("description"), Property->GetMetaData(TEXT("Description")));
		}

		Properties->SetObjectField(JsonKey, PropSchema);

		// Required 메타 → required 배열
		if (Property->HasMetaData(TEXT("Required")))
		{
			Required.Add(MakeShared<FJsonValueString>(JsonKey));
		}
	}

	Schema->SetObjectField(TEXT("properties"), Properties);

	if (Required.Num() > 0)
	{
		Schema->SetArrayField(TEXT("required"), Required);
	}

	return Schema;
}

bool UMcpServer::PopulateToolParams(const UScriptStruct* Struct, void* ToolMemory, const TSharedPtr<FJsonObject>& Arguments, FString& OutError) const
{
	if (!Arguments.IsValid())
	{
		return true;
	}

	for (TFieldIterator<FProperty> It(Struct); It; ++It)
	{
		FProperty* Property = *It;

		if (!Property->HasMetaData(TEXT("ToolParam")))
		{
			continue;
		}

		const FString JsonKey = GetParamJsonKey(Property);

		if (!Arguments->HasField(JsonKey))
		{
			if (Property->HasMetaData(TEXT("Required")))
			{
				OutError = FString::Printf(TEXT("Invalid params: '%s' is required"), *JsonKey);
				return false;
			}

			continue;
		}

		if (FStrProperty* StrProp = CastField<FStrProperty>(Property))
		{
			FString Value;
			if (!Arguments->TryGetStringField(JsonKey, Value))
			{
				OutError = FString::Printf(TEXT("Invalid params: '%s' must be a string"), *JsonKey);
				return false;
			}

			StrProp->SetPropertyValue_InContainer(ToolMemory, Value);
		}
		else if (FIntProperty* IntProp = CastField<FIntProperty>(Property))
		{
			double Value = 0.0;
			if (!Arguments->TryGetNumberField(JsonKey, Value))
			{
				OutError = FString::Printf(TEXT("Invalid params: '%s' must be a number"), *JsonKey);
				return false;
			}

			IntProp->SetPropertyValue_InContainer(ToolMemory, static_cast<int32>(Value));
		}
		else if (FInt64Property* Int64Prop = CastField<FInt64Property>(Property))
		{
			double Value = 0.0;
			if (!Arguments->TryGetNumberField(JsonKey, Value))
			{
				OutError = FString::Printf(TEXT("Invalid params: '%s' must be a number"), *JsonKey);
				return false;
			}

			Int64Prop->SetPropertyValue_InContainer(ToolMemory, static_cast<int64>(Value));
		}
		else if (FFloatProperty* FloatProp = CastField<FFloatProperty>(Property))
		{
			double Value = 0.0;
			if (!Arguments->TryGetNumberField(JsonKey, Value))
			{
				OutError = FString::Printf(TEXT("Invalid params: '%s' must be a number"), *JsonKey);
				return false;
			}

			FloatProp->SetPropertyValue_InContainer(ToolMemory, static_cast<float>(Value));
		}
		else if (FDoubleProperty* DoubleProp = CastField<FDoubleProperty>(Property))
		{
			double Value = 0.0;
			if (!Arguments->TryGetNumberField(JsonKey, Value))
			{
				OutError = FString::Printf(TEXT("Invalid params: '%s' must be a number"), *JsonKey);
				return false;
			}

			DoubleProp->SetPropertyValue_InContainer(ToolMemory, Value);
		}
		else if (FBoolProperty* BoolProp = CastField<FBoolProperty>(Property))
		{
			bool bValue = false;
			if (!Arguments->TryGetBoolField(JsonKey, bValue))
			{
				OutError = FString::Printf(TEXT("Invalid params: '%s' must be a boolean"), *JsonKey);
				return false;
			}

			BoolProp->SetPropertyValue_InContainer(ToolMemory, bValue);
		}
	}

	return true;
}

FString UMcpServer::GetParamJsonKey(const FProperty* Property)
{
	const FString MetaValue = Property->GetMetaData(TEXT("ToolParam"));

	// ToolParam="custom_key" 형태면 해당 값을, 아니면 프로퍼티 이름을 사용합니다
	if (!MetaValue.IsEmpty() && MetaValue != TEXT("TRUE"))
	{
		return MetaValue;
	}

	return Property->GetName();
}

FMcpResponse UMcpServer::ExecuteTool(const FString& Name, const TSharedPtr<FJsonObject>& Arguments, FString* OutInvalidParamsError)
{
	// 도구를 찾습니다
	UScriptStruct** FoundStruct = ToolMap.Find(Name);
	if (!FoundStruct)
	{
		return FMcpResponse::Failure(FString::Printf(TEXT("Unknown tool: %s"), *Name));
	}

	UScriptStruct* Struct = *FoundStruct;

	// 도구 구조체를 힙에 할당하고 초기화합니다
	void* Memory = FMemory::Malloc(Struct->GetStructureSize(), Struct->GetMinAlignment());
	Struct->InitializeStruct(Memory);

	FMcpTool* Tool = static_cast<FMcpTool*>(Memory);

	// ToolParam UPROPERTY에 arguments를 역직렬화합니다
	Tool->Args = Arguments;
	FString InvalidParamsError;
	if (!PopulateToolParams(Struct, Memory, Arguments, InvalidParamsError))
	{
		Struct->DestroyStruct(Memory);
		FMemory::Free(Memory);

		if (OutInvalidParamsError)
		{
			*OutInvalidParamsError = InvalidParamsError;
		}

		return FMcpResponse::Failure(InvalidParamsError);
	}

	FMcpResponse Response = Tool->Execute();

	// 구조체를 정리합니다
	Struct->DestroyStruct(Memory);
	FMemory::Free(Memory);

	return Response;
}

//-----------------------------------------------------------------------------
// JSON-RPC 유틸리티
//-----------------------------------------------------------------------------

TSharedPtr<FJsonObject> UMcpServer::MakeJsonRpcResponse(const TSharedPtr<FJsonValue>& Id, const TSharedPtr<FJsonObject>& Result)
{
	const TSharedPtr<FJsonObject> Response = MakeShared<FJsonObject>();
	Response->SetStringField(TEXT("jsonrpc"), TEXT("2.0"));
	SetJsonRpcId(Response, Id);
	Response->SetObjectField(TEXT("result"), Result);

	return Response;
}

TSharedPtr<FJsonObject> UMcpServer::MakeJsonRpcError(const TSharedPtr<FJsonValue>& Id, int32 Code, const FString& Message)
{
	const TSharedPtr<FJsonObject> ErrorObj = MakeShared<FJsonObject>();
	ErrorObj->SetNumberField(TEXT("code"), Code);
	ErrorObj->SetStringField(TEXT("message"), Message);

	const TSharedPtr<FJsonObject> Response = MakeShared<FJsonObject>();
	Response->SetStringField(TEXT("jsonrpc"), TEXT("2.0"));
	SetJsonRpcId(Response, Id);
	Response->SetObjectField(TEXT("error"), ErrorObj);

	return Response;
}

void UMcpServer::SetJsonRpcId(const TSharedPtr<FJsonObject>& Response, const TSharedPtr<FJsonValue>& Id)
{
	if (!Id.IsValid() || Id->Type == EJson::Null)
	{
		Response->SetField(TEXT("id"), MakeShared<FJsonValueNull>());
	}
	else if (Id->Type == EJson::String)
	{
		Response->SetStringField(TEXT("id"), Id->AsString());
	}
	else
	{
		Response->SetNumberField(TEXT("id"), Id->AsNumber());
	}
}

//-----------------------------------------------------------------------------
// 복구 힌트
//-----------------------------------------------------------------------------

TSharedPtr<FJsonObject> UMcpServer::LoadRecoveryHint() const
{
	FString StatusPath = FPaths::Combine(
		FPaths::ProjectSavedDir(), TEXT("UnrealAgent/task_status.json"));

	FString Content;
	if (!FFileHelper::LoadFileToString(Content, *StatusPath))
		return nullptr;

	TSharedPtr<FJsonObject> Status;
	TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Content);
	if (!FJsonSerializer::Deserialize(Reader, Status) || !Status.IsValid())
		return nullptr;

	FString TaskStatus;
	Status->TryGetStringField(TEXT("status"), TaskStatus);
	if (TaskStatus != TEXT("in_progress"))
		return nullptr;

	TSharedPtr<FJsonObject> Hint = MakeShared<FJsonObject>();
	FString TaskId, BpPath, SnapshotId;
	Status->TryGetStringField(TEXT("task_id"),        TaskId);
	Status->TryGetStringField(TEXT("blueprint_path"), BpPath);
	Status->TryGetStringField(TEXT("snapshot_id"),    SnapshotId);
	Hint->SetStringField(TEXT("task_id"),        TaskId);
	Hint->SetStringField(TEXT("blueprint_path"), BpPath);
	Hint->SetStringField(TEXT("snapshot_id"),    SnapshotId);

	return Hint;
}

FString UMcpServer::SerializeJson(const TSharedPtr<FJsonObject>& JsonObject)
{
	FString OutputString;
	const TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutputString, 0);
	FJsonSerializer::Serialize(JsonObject.ToSharedRef(), Writer);

	return OutputString;
}
