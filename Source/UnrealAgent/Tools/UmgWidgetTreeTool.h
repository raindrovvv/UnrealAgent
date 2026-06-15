#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "UmgWidgetTreeTool.generated.h"

USTRUCT(meta=(McpTool="umg_widget_tree"))
struct FUmgWidgetTreeTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="create_wbp | delete_wbp | reparent_wbp | add_widget | delete_widget | move_widget | get_tree | compile_wbp | set_widget_property | get_widget_property | get_widget_schema"))
	FString Operation;

	UPROPERTY(meta=(ToolParam="wbp_path", Required,
	                Description="Widget Blueprint asset path (e.g. /Game/UI/WBP_MyWidget or /PluginName/UI/WBP_MyWidget)"))
	FString WbpPath;

	UPROPERTY(meta=(ToolParam="root_widget_class",
	                Description="Root widget class for create_wbp (e.g. SizeBox, VerticalBox, HorizontalBox, Border, Overlay, CanvasPanel)"))
	FString RootWidgetClass;

	UPROPERTY(meta=(ToolParam="parent_class",
	                Description="C++ parent class full path for reparent_wbp (e.g. /Script/GAS.GS_TestStatRowWidget). Use 'UserWidget' for default."))
	FString ParentClass;

	UPROPERTY(meta=(ToolParam="parent_widget",
	                Description="Name of parent widget in tree for add_widget. Empty string means use root widget."))
	FString ParentWidget;

	UPROPERTY(meta=(ToolParam="widget_class",
	                Description="Widget class for add_widget (e.g. CommonTextBlock, Slider, EditableTextBox, SizeBox, HorizontalBox, VerticalBox, Border)"))
	FString WidgetClass;

	UPROPERTY(meta=(ToolParam="widget_name",
	                Description="Name for new widget (used for BindWidget). Must match C++ UPROPERTY name exactly."))
	FString WidgetName;

	UPROPERTY(meta=(ToolParam="property_name",
	                Description="PROP_TEXT | PROP_FONT_SIZE | PROP_FONT_TYPEFACE | PROP_COLOR_HEX | PROP_JUSTIFICATION | PROP_AUTO_WRAP_TEXT | PROP_MIN_DESIRED_WIDTH | PROP_MIN_DESIRED_HEIGHT | PROP_WIDTH_OVERRIDE | PROP_WIDTH_OVERRIDE_ENABLED | PROP_HEIGHT_OVERRIDE | PROP_HEIGHT_OVERRIDE_ENABLED | PROP_BRUSH_COLOR_HEX | PROP_BRUSH_ALPHA | PROP_PADDING | PROP_PADDING_LEFT | PROP_PADDING_TOP | PROP_PADDING_RIGHT | PROP_PADDING_BOTTOM | PROP_SIZE_X | PROP_SIZE_Y | PROP_WIDTH | PROP_HEIGHT | PROP_ACTIVE_WIDGET_INDEX | PROP_IS_SELECTABLE | PROP_BUTTON_TEXT | PROP_IS_TOGGLEABLE | PROP_ACCENT_COLOR_HEX | PROP_SELECTED_BACKGROUND_ALPHA | SLOT_SIZE_RULE | SLOT_FILL_VALUE | SLOT_H_ALIGNMENT | SLOT_V_ALIGNMENT | SLOT_PADDING. Direct UPROPERTY names (e.g. ButtonText, BackgroundTint) are also supported via UE reflection."))
	FString PropertyName;

	UPROPERTY(meta=(ToolParam="property_value",
	                Description="Value for the property. Colors: #RRGGBB or #RRGGBBAA. Padding: 'all' or 'h,v' or 'l,t,r,b'. SizeRule: Auto|Fill. Alignment: Left|Center|Right|Fill (H), Top|Center|Bottom|Fill (V)."))
	FString PropertyValue;

	UPROPERTY(meta=(ToolParam="skip_compile",
	                Description="If 'true', skip compile+save after set_widget_property (for batch mode). Call compile_wbp when done."))
	FString SkipCompile;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	FMcpResponse HandleCreateWbp();
	FMcpResponse HandleDeleteWbp();
	FMcpResponse HandleReparentWbp();
	FMcpResponse HandleAddWidget();
	FMcpResponse HandleGetTree();
	FMcpResponse HandleCompileWbp();
	FMcpResponse HandleSetWidgetProperty();
	FMcpResponse HandleDeleteWidget();
	FMcpResponse HandleMoveWidget();
	FMcpResponse HandleGetWidgetProperty();
	FMcpResponse HandleGetWidgetSchema();

	UClass* FindWidgetClass(const FString& ClassName) const;
	FString SerializeTree(class UWidget* Widget, int32 Depth = 0) const;

	static bool ParseHexColor(const FString& Hex, FLinearColor& OutColor);
	static bool ParseMargin(const FString& Value, FMargin& OutMargin);
	static EHorizontalAlignment HAlignFromString(const FString& Value);
	static EVerticalAlignment VAlignFromString(const FString& Value);
};
