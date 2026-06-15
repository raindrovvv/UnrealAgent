#pragma once
#include "CoreMinimal.h"
#include "McpTypes.h"
#include "BlueprintGraphOpsTool.generated.h"

/**
 * Blueprint 그래프 스냅샷/복구 도구입니다.
 *
 * operation별 동작:
 * - snapshot : Blueprint 그래프 → JSON 파일 저장 + task_status.json 기록
 * - restore  : JSON에서 Blueprint 그래프 복구
 * - complete : task_status.json 완료/실패 기록
 * - diff     : 현재 그래프와 스냅샷 노드 수 비교
 * - list     : 사용 가능한 스냅샷 목록 반환
 */
USTRUCT(meta=(McpTool="blueprint_graph_ops"))
struct FBlueprintGraphOpsTool : public FMcpTool
{
    GENERATED_BODY()

    UPROPERTY(meta=(ToolParam="operation", Required,
                    Description="snapshot | restore | complete | diff | list"))
    FString Operation;

    UPROPERTY(meta=(ToolParam="blueprint_path",
                    Description="Blueprint asset path (e.g. /Game/Game/MyBP)"))
    FString BlueprintPath;

    UPROPERTY(meta=(ToolParam="task_id",
                    Description="Harness task ID"))
    FString TaskId;

    UPROPERTY(meta=(ToolParam="snapshot_id",
                    Description="Snapshot filename returned by snapshot operation"))
    FString SnapshotId;

    UPROPERTY(meta=(ToolParam="status",
                    Description="completed | failed (for complete operation)"))
    FString Status;

    virtual FString ToolDescription() const override;
    virtual FMcpResponse Execute() override;

private:
    FString GetSnapshotDir() const;
    FString GetStatusFilePath() const;

    FMcpResponse HandleSnapshot();
    FMcpResponse HandleRestore();
    FMcpResponse HandleComplete();
    FMcpResponse HandleDiff();
    FMcpResponse HandleList();

    TArray<TSharedPtr<FJsonValue>> SerializeNodes(UEdGraph* Graph) const;
    TArray<TSharedPtr<FJsonValue>> SerializeConnections(UEdGraph* Graph) const;
    bool RestoreGraph(UBlueprint* BP, const TSharedPtr<FJsonObject>& SnapJson);
};
