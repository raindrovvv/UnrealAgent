#include "BlueprintGraphOpsTool.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraph/EdGraphPin.h"
#include "Engine/Blueprint.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "HAL/FileManager.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(BlueprintGraphOpsTool)

FString FBlueprintGraphOpsTool::ToolDescription() const
{
    return TEXT("Blueprint graph snapshot, restore, diff, and list for crash recovery. "
                 "Call snapshot before editing, restore after crash, complete when done.");
}

FMcpResponse FBlueprintGraphOpsTool::Execute()
{
    if (Operation == TEXT("snapshot"))  return HandleSnapshot();
    if (Operation == TEXT("restore"))   return HandleRestore();
    if (Operation == TEXT("complete"))  return HandleComplete();
    if (Operation == TEXT("diff"))      return HandleDiff();
    if (Operation == TEXT("list"))      return HandleList();
    return FMcpResponse::Failure(FString::Printf(TEXT("Unknown operation: %s"), *Operation));
}

FString FBlueprintGraphOpsTool::GetSnapshotDir() const
{
    return FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("UnrealAgent/Snapshots"));
}

FString FBlueprintGraphOpsTool::GetStatusFilePath() const
{
    return FPaths::Combine(FPaths::ProjectSavedDir(), TEXT("UnrealAgent/task_status.json"));
}

FMcpResponse FBlueprintGraphOpsTool::HandleSnapshot()
{
    if (BlueprintPath.IsEmpty() || TaskId.IsEmpty())
        return FMcpResponse::Failure(TEXT("blueprint_path and task_id required"));

    UBlueprint* BP = Cast<UBlueprint>(StaticLoadObject(UBlueprint::StaticClass(), nullptr, *BlueprintPath));
    if (!BP)
        return FMcpResponse::Failure(FString::Printf(TEXT("Blueprint not found: %s"), *BlueprintPath));

    UEdGraph* Graph = nullptr;
    for (UEdGraph* G : BP->UbergraphPages)
    {
        if (G->GetName() == TEXT("EventGraph")) { Graph = G; break; }
    }
    if (!Graph && BP->UbergraphPages.Num() > 0)
        Graph = BP->UbergraphPages[0];
    if (!Graph)
        return FMcpResponse::Failure(TEXT("No graph found in blueprint"));

    FString BPName    = FPaths::GetBaseFilename(BlueprintPath);
    FString Timestamp = FDateTime::Now().ToString(TEXT("%Y%m%d_%H%M%S"));
    FString FileName  = FString::Printf(TEXT("%s_%s.json"), *Timestamp, *BPName);
    FString FilePath  = FPaths::Combine(GetSnapshotDir(), FileName);

    TSharedPtr<FJsonObject> Snap = MakeShared<FJsonObject>();
    Snap->SetStringField(TEXT("blueprint_path"), BlueprintPath);
    Snap->SetStringField(TEXT("timestamp"),      FDateTime::UtcNow().ToString());
    Snap->SetStringField(TEXT("task_id"),        TaskId);
    Snap->SetStringField(TEXT("graph_name"),     Graph->GetName());
    Snap->SetArrayField (TEXT("nodes"),          SerializeNodes(Graph));
    Snap->SetArrayField (TEXT("connections"),    SerializeConnections(Graph));

    FString JsonStr;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&JsonStr);
    FJsonSerializer::Serialize(Snap.ToSharedRef(), Writer);

    IFileManager::Get().MakeDirectory(*GetSnapshotDir(), true);
    if (!FFileHelper::SaveStringToFile(JsonStr, *FilePath))
        return FMcpResponse::Failure(TEXT("Failed to save snapshot file"));

    // task_status.json 기록
    TSharedPtr<FJsonObject> StatusObj = MakeShared<FJsonObject>();
    StatusObj->SetStringField(TEXT("task_id"),        TaskId);
    StatusObj->SetStringField(TEXT("status"),         TEXT("in_progress"));
    StatusObj->SetStringField(TEXT("blueprint_path"), BlueprintPath);
    StatusObj->SetStringField(TEXT("snapshot_id"),    FileName);
    StatusObj->SetStringField(TEXT("started_at"),     FDateTime::UtcNow().ToString());

    FString StatusStr;
    TSharedRef<TJsonWriter<>> SW = TJsonWriterFactory<>::Create(&StatusStr);
    FJsonSerializer::Serialize(StatusObj.ToSharedRef(), SW);
    FFileHelper::SaveStringToFile(StatusStr, *GetStatusFilePath());

    return FMcpResponse::Success(FileName);
}

FMcpResponse FBlueprintGraphOpsTool::HandleComplete()
{
    if (TaskId.IsEmpty())
        return FMcpResponse::Failure(TEXT("task_id required"));

    FString Content;
    if (!FFileHelper::LoadFileToString(Content, *GetStatusFilePath()))
        return FMcpResponse::Failure(TEXT("task_status.json not found"));

    TSharedPtr<FJsonObject> StatusObj;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Content);
    if (!FJsonSerializer::Deserialize(Reader, StatusObj) || !StatusObj.IsValid())
        return FMcpResponse::Failure(TEXT("Failed to parse task_status.json"));

    FString StoredId;
    StatusObj->TryGetStringField(TEXT("task_id"), StoredId);
    if (StoredId != TaskId)
        return FMcpResponse::Failure(TEXT("task_id mismatch"));

    FString FinalStatus = Status.IsEmpty() ? TEXT("completed") : Status;
    StatusObj->SetStringField(TEXT("status"),       FinalStatus);
    StatusObj->SetStringField(TEXT("completed_at"), FDateTime::UtcNow().ToString());

    FString OutStr;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
    FJsonSerializer::Serialize(StatusObj.ToSharedRef(), Writer);
    FFileHelper::SaveStringToFile(OutStr, *GetStatusFilePath());

    return FMcpResponse::Success(FString::Printf(TEXT("Task %s → %s"), *TaskId, *FinalStatus));
}

FMcpResponse FBlueprintGraphOpsTool::HandleRestore()
{
    if (SnapshotId.IsEmpty())
        return FMcpResponse::Failure(TEXT("snapshot_id required"));

    FString FilePath = FPaths::Combine(GetSnapshotDir(), SnapshotId);
    FString Content;
    if (!FFileHelper::LoadFileToString(Content, *FilePath))
        return FMcpResponse::Failure(FString::Printf(TEXT("Snapshot not found: %s"), *SnapshotId));

    TSharedPtr<FJsonObject> SnapJson;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Content);
    if (!FJsonSerializer::Deserialize(Reader, SnapJson) || !SnapJson.IsValid())
        return FMcpResponse::Failure(TEXT("Failed to parse snapshot JSON"));

    FString BpPath;
    SnapJson->TryGetStringField(TEXT("blueprint_path"), BpPath);

    UBlueprint* BP = Cast<UBlueprint>(StaticLoadObject(UBlueprint::StaticClass(), nullptr, *BpPath));
    if (!BP)
        return FMcpResponse::Failure(FString::Printf(TEXT("Blueprint not found: %s"), *BpPath));

    if (!RestoreGraph(BP, SnapJson))
        return FMcpResponse::Failure(TEXT("Failed to restore graph"));

    FKismetEditorUtilities::CompileBlueprint(BP);
    return FMcpResponse::Success(FString::Printf(TEXT("Restored %s from %s"), *BpPath, *SnapshotId));
}

FMcpResponse FBlueprintGraphOpsTool::HandleDiff()
{
    if (BlueprintPath.IsEmpty() || SnapshotId.IsEmpty())
        return FMcpResponse::Failure(TEXT("blueprint_path and snapshot_id required"));

    FString FilePath = FPaths::Combine(GetSnapshotDir(), SnapshotId);
    FString Content;
    if (!FFileHelper::LoadFileToString(Content, *FilePath))
        return FMcpResponse::Failure(FString::Printf(TEXT("Snapshot not found: %s"), *SnapshotId));

    TSharedPtr<FJsonObject> SnapJson;
    TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Content);
    FJsonSerializer::Deserialize(Reader, SnapJson);

    UBlueprint* BP = Cast<UBlueprint>(StaticLoadObject(UBlueprint::StaticClass(), nullptr, *BlueprintPath));
    if (!BP)
        return FMcpResponse::Failure(FString::Printf(TEXT("Blueprint not found: %s"), *BlueprintPath));

    UEdGraph* Graph = BP->UbergraphPages.Num() > 0 ? BP->UbergraphPages[0] : nullptr;
    if (!Graph)
        return FMcpResponse::Failure(TEXT("No graph found"));

    const TArray<TSharedPtr<FJsonValue>>* SnapNodes;
    int32 SnapCount = (SnapJson.IsValid() && SnapJson->TryGetArrayField(TEXT("nodes"), SnapNodes))
        ? SnapNodes->Num() : 0;

    TSharedPtr<FJsonObject> Out = MakeShared<FJsonObject>();
    Out->SetNumberField(TEXT("snapshot_node_count"), SnapCount);
    Out->SetNumberField(TEXT("current_node_count"),  Graph->Nodes.Num());
    Out->SetNumberField(TEXT("node_diff"),           Graph->Nodes.Num() - SnapCount);

    FString OutStr;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
    FJsonSerializer::Serialize(Out.ToSharedRef(), Writer);
    return FMcpResponse::Success(OutStr);
}

FMcpResponse FBlueprintGraphOpsTool::HandleList()
{
    IFileManager::Get().MakeDirectory(*GetSnapshotDir(), true);

    TArray<FString> Files;
    IFileManager::Get().FindFiles(Files, *(GetSnapshotDir() / TEXT("*.json")), true, false);

    if (!BlueprintPath.IsEmpty())
    {
        FString BPName = FPaths::GetBaseFilename(BlueprintPath);
        Files = Files.FilterByPredicate([&](const FString& F) {
            return F.Contains(BPName);
        });
    }

    TArray<TSharedPtr<FJsonValue>> Results;
    for (const FString& F : Files)
        Results.Add(MakeShared<FJsonValueString>(F));

    TSharedPtr<FJsonObject> Out = MakeShared<FJsonObject>();
    Out->SetArrayField(TEXT("snapshots"), Results);
    Out->SetNumberField(TEXT("count"), Results.Num());

    FString OutStr;
    TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
    FJsonSerializer::Serialize(Out.ToSharedRef(), Writer);
    return FMcpResponse::Success(OutStr);
}

TArray<TSharedPtr<FJsonValue>> FBlueprintGraphOpsTool::SerializeNodes(UEdGraph* Graph) const
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (UEdGraphNode* Node : Graph->Nodes)
    {
        if (!Node) continue;
        TSharedPtr<FJsonObject> N = MakeShared<FJsonObject>();
        N->SetStringField(TEXT("guid"),  Node->NodeGuid.ToString());
        N->SetStringField(TEXT("class"), Node->GetClass()->GetName());
        N->SetNumberField(TEXT("pos_x"), Node->NodePosX);
        N->SetNumberField(TEXT("pos_y"), Node->NodePosY);
        N->SetStringField(TEXT("title"), Node->GetNodeTitle(ENodeTitleType::FullTitle).ToString());
        Result.Add(MakeShared<FJsonValueObject>(N));
    }
    return Result;
}

TArray<TSharedPtr<FJsonValue>> FBlueprintGraphOpsTool::SerializeConnections(UEdGraph* Graph) const
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (UEdGraphNode* Node : Graph->Nodes)
    {
        if (!Node) continue;
        for (UEdGraphPin* Pin : Node->Pins)
        {
            if (!Pin || Pin->Direction != EGPD_Output) continue;
            for (UEdGraphPin* Linked : Pin->LinkedTo)
            {
                if (!Linked || !Linked->GetOwningNode()) continue;
                TSharedPtr<FJsonObject> C = MakeShared<FJsonObject>();
                C->SetStringField(TEXT("source_node"), Node->NodeGuid.ToString());
                C->SetStringField(TEXT("source_pin"),  Pin->PinName.ToString());
                C->SetStringField(TEXT("target_node"), Linked->GetOwningNode()->NodeGuid.ToString());
                C->SetStringField(TEXT("target_pin"),  Linked->PinName.ToString());
                Result.Add(MakeShared<FJsonValueObject>(C));
            }
        }
    }
    return Result;
}

bool FBlueprintGraphOpsTool::RestoreGraph(UBlueprint* BP, const TSharedPtr<FJsonObject>& SnapJson)
{
    FString GraphName;
    SnapJson->TryGetStringField(TEXT("graph_name"), GraphName);

    UEdGraph* Graph = nullptr;
    for (UEdGraph* G : BP->UbergraphPages)
    {
        if (G->GetName() == GraphName || GraphName.IsEmpty()) { Graph = G; break; }
    }
    if (!Graph) return false;

    const TArray<TSharedPtr<FJsonValue>>* SnapNodes;
    if (!SnapJson->TryGetArrayField(TEXT("nodes"), SnapNodes))
        return false;

    // 스냅샷에 없는 노드 제거 (새로 추가된 노드 롤백)
    TSet<FString> SnapGuids;
    for (const TSharedPtr<FJsonValue>& V : *SnapNodes)
    {
        FString Guid;
        if (V->AsObject()->TryGetStringField(TEXT("guid"), Guid))
            SnapGuids.Add(Guid);
    }

    TArray<UEdGraphNode*> ToRemove;
    for (UEdGraphNode* Node : Graph->Nodes)
    {
        if (Node && !SnapGuids.Contains(Node->NodeGuid.ToString()))
            ToRemove.Add(Node);
    }
    for (UEdGraphNode* Node : ToRemove)
        Graph->RemoveNode(Node);

    return true;
}
