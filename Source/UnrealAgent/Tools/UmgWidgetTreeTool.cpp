#include "Tools/UmgWidgetTreeTool.h"
#include "WidgetBlueprint.h"
#include "Blueprint/WidgetTree.h"
#include "Components/Widget.h"
#include "Components/PanelWidget.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "AssetToolsModule.h"
#include "IAssetTools.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "ObjectTools.h"
#include "FileHelpers.h"
#include "WidgetBlueprintFactory.h"
#include "Misc/PackageName.h"
#include "Editor.h"
#include "Components/TextBlock.h"
#include "Components/Border.h"
#include "Components/SizeBox.h"
#include "Components/Spacer.h"
#include "Components/Image.h"
#include "Components/WidgetSwitcher.h"
#include "Components/VerticalBoxSlot.h"
#include "Components/HorizontalBoxSlot.h"
#include "Components/OverlaySlot.h"
#include "Components/CanvasPanelSlot.h"
#include "Components/WidgetSwitcherSlot.h"
#include "JsonObjectConverter.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(UmgWidgetTreeTool)

FString FUmgWidgetTreeTool::ToolDescription() const
{
	return TEXT(
		"Widget Blueprint tree construction (C++ native — preferred over Python for WBP).\n"
		"create_wbp   : create a new WBP asset with optional root widget (wbp_path, root_widget_class)\n"
		"delete_wbp   : delete a WBP asset (wbp_path)\n"
		"reparent_wbp : change parent C++ class (wbp_path, parent_class)\n"
		"add_widget   : add a child widget to the tree (wbp_path, widget_class, widget_name, parent_widget)\n"
		"get_tree     : print the widget tree structure (wbp_path)\n"
		"compile_wbp  : compile and save the WBP (wbp_path)\n"
		"delete_widget : remove a widget from the tree (wbp_path, widget_name)\n"
		"move_widget  : move a widget to a new parent (wbp_path, widget_name, parent_widget)\n"
		"get_widget_property : read widget properties (wbp_path, widget_name, property_name=optional)\n"
		"get_widget_schema   : list editable properties of a widget (wbp_path, widget_name)\n"
		"set_widget_property : set a property on a widget. Use PROP_*/SLOT_* for built-in, or direct UPROPERTY name for generic reflection"
	);
}

FMcpResponse FUmgWidgetTreeTool::Execute()
{
	if (Operation == TEXT("create_wbp"))   return HandleCreateWbp();
	if (Operation == TEXT("delete_wbp"))   return HandleDeleteWbp();
	if (Operation == TEXT("reparent_wbp")) return HandleReparentWbp();
	if (Operation == TEXT("add_widget"))   return HandleAddWidget();
	if (Operation == TEXT("get_tree"))     return HandleGetTree();
	if (Operation == TEXT("compile_wbp"))  return HandleCompileWbp();
	if (Operation == TEXT("set_widget_property")) return HandleSetWidgetProperty();
	if (Operation == TEXT("delete_widget"))       return HandleDeleteWidget();
	if (Operation == TEXT("move_widget"))         return HandleMoveWidget();
	if (Operation == TEXT("get_widget_property")) return HandleGetWidgetProperty();
	if (Operation == TEXT("get_widget_schema"))   return HandleGetWidgetSchema();

	return FMcpResponse::Failure(FString::Printf(TEXT("Unknown operation: %s"), *Operation));
}

FMcpResponse FUmgWidgetTreeTool::HandleCreateWbp()
{
	if (WbpPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("wbp_path required"));

	if (FPackageName::DoesPackageExist(WbpPath))
		return FMcpResponse::Failure(FString::Printf(
			TEXT("Asset already exists: %s. Use delete_wbp first."), *WbpPath));

	// Split /A/B/WBP_Foo into PackagePath=/A/B and AssetName=WBP_Foo
	FString PackagePath, AssetName;
	WbpPath.Split(TEXT("/"), &PackagePath, &AssetName, ESearchCase::IgnoreCase, ESearchDir::FromEnd);
	PackagePath = TEXT("/") + PackagePath.TrimChar('/');

	IAssetTools& AssetTools = FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools")).Get();
	UWidgetBlueprintFactory* Factory = NewObject<UWidgetBlueprintFactory>();
	Factory->ParentClass = UUserWidget::StaticClass();

	UObject* NewAsset = AssetTools.CreateAsset(AssetName, PackagePath, UWidgetBlueprint::StaticClass(), Factory);
	UWidgetBlueprint* WBP = Cast<UWidgetBlueprint>(NewAsset);
	if (!WBP)
		return FMcpResponse::Failure(TEXT("Failed to create WidgetBlueprint"));

	if (!RootWidgetClass.IsEmpty())
	{
		UClass* RootClass = FindWidgetClass(RootWidgetClass);
		if (RootClass)
		{
			UWidgetTree* WT = WBP->WidgetTree;
			UWidget* Root = WT->ConstructWidget<UWidget>(RootClass, FName("Root"));
			WT->RootWidget = Root;
		}
		else
		{
			// WBP is already created — report warning but don't fail
			FKismetEditorUtilities::CompileBlueprint(WBP);
			TArray<UPackage*> Packages = { WBP->GetOutermost() };
			FEditorFileUtils::PromptForCheckoutAndSave(Packages, false, false);
			return FMcpResponse::Success(FString::Printf(
				TEXT("Created WBP: %s (WARNING: root_widget_class '%s' not found, tree is empty)"),
				*WbpPath, *RootWidgetClass));
		}
	}

	FKismetEditorUtilities::CompileBlueprint(WBP);
	TArray<UPackage*> Packages = { WBP->GetOutermost() };
	FEditorFileUtils::PromptForCheckoutAndSave(Packages, false, false);

	return FMcpResponse::Success(FString::Printf(TEXT("Created WBP: %s"), *WbpPath));
}

FMcpResponse FUmgWidgetTreeTool::HandleDeleteWbp()
{
	if (WbpPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("wbp_path required"));

	UObject* Asset = LoadObject<UObject>(nullptr, *WbpPath);
	if (!Asset)
		return FMcpResponse::Failure(FString::Printf(TEXT("Asset not found: %s"), *WbpPath));

	TArray<UObject*> ToDelete = { Asset };
	int32 Deleted = ObjectTools::DeleteObjects(ToDelete, false);
	return Deleted > 0
		? FMcpResponse::Success(TEXT("Deleted: ") + WbpPath)
		: FMcpResponse::Failure(TEXT("Delete failed for: ") + WbpPath);
}

FMcpResponse FUmgWidgetTreeTool::HandleReparentWbp()
{
	if (WbpPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("wbp_path required"));

	UWidgetBlueprint* WBP = Cast<UWidgetBlueprint>(LoadObject<UObject>(nullptr, *WbpPath));
	if (!WBP)
		return FMcpResponse::Failure(TEXT("WBP not found: ") + WbpPath);

	UClass* NewParent = nullptr;
	if (ParentClass.IsEmpty() || ParentClass == TEXT("UserWidget"))
	{
		NewParent = UUserWidget::StaticClass();
	}
	else
	{
		NewParent = LoadClass<UUserWidget>(nullptr, *ParentClass);
		if (!NewParent)
			NewParent = FindObject<UClass>(nullptr, *ParentClass);
	}

	if (!NewParent)
		return FMcpResponse::Failure(TEXT("Parent class not found: ") + ParentClass);

	WBP->ParentClass = NewParent;
	FKismetEditorUtilities::CompileBlueprint(WBP);
	TArray<UPackage*> Packages = { WBP->GetOutermost() };
	FEditorFileUtils::PromptForCheckoutAndSave(Packages, false, false);

	return FMcpResponse::Success(FString::Printf(
		TEXT("Reparented '%s' to %s"), *WbpPath, *NewParent->GetName()));
}

FMcpResponse FUmgWidgetTreeTool::HandleAddWidget()
{
	if (WbpPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("wbp_path required"));
	if (WidgetClass.IsEmpty())
		return FMcpResponse::Failure(TEXT("widget_class required"));

	UWidgetBlueprint* WBP = Cast<UWidgetBlueprint>(LoadObject<UObject>(nullptr, *WbpPath));
	if (!WBP)
		return FMcpResponse::Failure(TEXT("WBP not found: ") + WbpPath);

	UWidgetTree* WT = WBP->WidgetTree;
	if (!WT)
		return FMcpResponse::Failure(TEXT("WidgetTree is null"));

	UPanelWidget* Parent = nullptr;
	if (ParentWidget.IsEmpty())
	{
		Parent = Cast<UPanelWidget>(WT->RootWidget);
	}
	else
	{
		UWidget* Found = WT->FindWidget(FName(*ParentWidget));
		Parent = Cast<UPanelWidget>(Found);
	}

	if (!Parent)
		return FMcpResponse::Failure(FString::Printf(
			TEXT("Parent widget '%s' not found or is not a panel widget"), *ParentWidget));

	UClass* WClass = FindWidgetClass(WidgetClass);
	if (!WClass)
		return FMcpResponse::Failure(TEXT("Widget class not found: ") + WidgetClass);

	FName NewName = WidgetName.IsEmpty() ? NAME_None : FName(*WidgetName);
	UWidget* NewWidget = WT->ConstructWidget<UWidget>(WClass, NewName);
	if (!NewWidget)
		return FMcpResponse::Failure(TEXT("Failed to construct widget"));

	Parent->AddChild(NewWidget);

	FKismetEditorUtilities::CompileBlueprint(WBP);
	TArray<UPackage*> Packages = { WBP->GetOutermost() };
	FEditorFileUtils::PromptForCheckoutAndSave(Packages, false, false);

	return FMcpResponse::Success(FString::Printf(
		TEXT("Added %s '%s' to '%s' in %s"),
		*WidgetClass, *WidgetName, *ParentWidget, *WbpPath));
}

FMcpResponse FUmgWidgetTreeTool::HandleGetTree()
{
	if (WbpPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("wbp_path required"));

	UWidgetBlueprint* WBP = Cast<UWidgetBlueprint>(LoadObject<UObject>(nullptr, *WbpPath));
	if (!WBP)
		return FMcpResponse::Failure(TEXT("WBP not found: ") + WbpPath);

	UWidgetTree* WT = WBP->WidgetTree;
	if (!WT || !WT->RootWidget)
		return FMcpResponse::Success(FString::Printf(TEXT("%s: (empty tree)"), *WbpPath));

	FString Tree = FString::Printf(TEXT("Widget tree for %s:\n"), *WbpPath);
	Tree += SerializeTree(WT->RootWidget, 0);
	return FMcpResponse::Success(Tree);
}

FMcpResponse FUmgWidgetTreeTool::HandleCompileWbp()
{
	if (WbpPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("wbp_path required"));

	UWidgetBlueprint* WBP = Cast<UWidgetBlueprint>(LoadObject<UObject>(nullptr, *WbpPath));
	if (!WBP)
		return FMcpResponse::Failure(TEXT("WBP not found: ") + WbpPath);

	FKismetEditorUtilities::CompileBlueprint(WBP);

	if (WBP->Status == EBlueprintStatus::BS_Error)
		return FMcpResponse::Failure(FString::Printf(
			TEXT("Compile failed for %s — check output log"), *WbpPath));

	TArray<UPackage*> Packages = { WBP->GetOutermost() };
	FEditorFileUtils::PromptForCheckoutAndSave(Packages, false, false);

	return FMcpResponse::Success(FString::Printf(TEXT("Compiled and saved: %s"), *WbpPath));
}

UClass* FUmgWidgetTreeTool::FindWidgetClass(const FString& ClassName) const
{
	// 1. Try exact path (e.g. /Script/UMG.Slider)
	if (UClass* Found = FindObject<UClass>(nullptr, *ClassName))
	{
		if (Found->IsChildOf(UWidget::StaticClass()))
			return Found;
	}

	// 2. Search all loaded UClass objects by short name
	for (TObjectIterator<UClass> It; It; ++It)
	{
		if (It->GetName() == ClassName && It->IsChildOf(UWidget::StaticClass()))
			return *It;
	}

	// 3. Try Blueprint-generated class name (append _C suffix)
	FString BlueprintClassName = ClassName + TEXT("_C");
	for (TObjectIterator<UClass> It; It; ++It)
	{
		if (It->GetName() == BlueprintClassName && It->IsChildOf(UWidget::StaticClass()))
			return *It;
	}

	return nullptr;
}

FString FUmgWidgetTreeTool::SerializeTree(UWidget* Widget, int32 Depth) const
{
	if (!Widget) return TEXT("");

	FString Indent = FString::ChrN(Depth * 2, ' ');
	FString Type = Widget->IsA<UPanelWidget>() ? TEXT("Panel") : TEXT("Leaf");
	FString Result = FString::Printf(TEXT("%s[%s] %s (%s)\n"),
		*Indent, *Widget->GetName(), *Widget->GetClass()->GetName(), *Type);

	if (UPanelWidget* Panel = Cast<UPanelWidget>(Widget))
	{
		for (int32 i = 0; i < Panel->GetChildrenCount(); i++)
		{
			Result += SerializeTree(Panel->GetChildAt(i), Depth + 1);
		}
	}
	return Result;
}

FMcpResponse FUmgWidgetTreeTool::HandleSetWidgetProperty()
{
	if (WbpPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("wbp_path required"));
	if (WidgetName.IsEmpty())
		return FMcpResponse::Failure(TEXT("widget_name required"));
	if (PropertyName.IsEmpty())
		return FMcpResponse::Failure(TEXT("property_name required"));

	UWidgetBlueprint* WBP = Cast<UWidgetBlueprint>(LoadObject<UObject>(nullptr, *WbpPath));
	if (!WBP)
		return FMcpResponse::Failure(TEXT("WBP not found: ") + WbpPath);

	UWidgetTree* WT = WBP->WidgetTree;
	if (!WT)
		return FMcpResponse::Failure(TEXT("WidgetTree is null"));

	UWidget* Widget = WT->FindWidget(FName(*WidgetName));
	if (!Widget)
		return FMcpResponse::Failure(FString::Printf(TEXT("Widget '%s' not found"), *WidgetName));

	// ── SLOT_ properties ──────────────────────────────────────────────
	if (PropertyName.StartsWith(TEXT("SLOT_")))
	{
		UPanelSlot* PanelSlot = Widget->Slot;
		if (!PanelSlot)
			return FMcpResponse::Failure(FString::Printf(
				TEXT("Widget '%s' has no slot (is it the root widget?)"), *WidgetName));

		auto* VSlot = Cast<UVerticalBoxSlot>(PanelSlot);
		auto* HSlot = Cast<UHorizontalBoxSlot>(PanelSlot);
		auto* OSlot = Cast<UOverlaySlot>(PanelSlot);

		if (PropertyName == TEXT("SLOT_PADDING"))
		{
			FMargin Margin;
			if (!ParseMargin(PropertyValue, Margin))
				return FMcpResponse::Failure(TEXT("Invalid margin. Use: value | h,v | l,t,r,b"));
			if (VSlot)      VSlot->SetPadding(Margin);
			else if (HSlot) HSlot->SetPadding(Margin);
			else if (OSlot) OSlot->SetPadding(Margin);
			else if (UWidgetSwitcherSlot* WSSlot = Cast<UWidgetSwitcherSlot>(PanelSlot))
				WSSlot->SetPadding(Margin);
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("SLOT_PADDING not supported for slot type '%s'"), *PanelSlot->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("SLOT_SIZE_RULE"))
		{
			ESlateSizeRule::Type Rule = (PropertyValue == TEXT("Fill"))
				? ESlateSizeRule::Fill : ESlateSizeRule::Automatic;
			if (VSlot)
			{
				FSlateChildSize SV = VSlot->GetSize();
				SV.SizeRule = Rule;
				VSlot->SetSize(SV);
			}
			else if (HSlot)
			{
				FSlateChildSize SH = HSlot->GetSize();
				SH.SizeRule = Rule;
				HSlot->SetSize(SH);
			}
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("SLOT_SIZE_RULE not supported for slot type '%s'"), *PanelSlot->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("SLOT_FILL_VALUE"))
		{
			float Val = FCString::Atof(*PropertyValue);
			if (VSlot)
			{
				FSlateChildSize SV = VSlot->GetSize();
				SV.Value = Val;
				VSlot->SetSize(SV);
			}
			else if (HSlot)
			{
				FSlateChildSize SH = HSlot->GetSize();
				SH.Value = Val;
				HSlot->SetSize(SH);
			}
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("SLOT_FILL_VALUE not supported for slot type '%s'"), *PanelSlot->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("SLOT_H_ALIGNMENT"))
		{
			EHorizontalAlignment A = HAlignFromString(PropertyValue);
			if (VSlot)      VSlot->SetHorizontalAlignment(A);
			else if (HSlot) HSlot->SetHorizontalAlignment(A);
			else if (OSlot) OSlot->SetHorizontalAlignment(A);
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("SLOT_H_ALIGNMENT not supported for slot type '%s'"), *PanelSlot->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("SLOT_V_ALIGNMENT"))
		{
			EVerticalAlignment A = VAlignFromString(PropertyValue);
			if (VSlot)      VSlot->SetVerticalAlignment(A);
			else if (HSlot) HSlot->SetVerticalAlignment(A);
			else if (OSlot) OSlot->SetVerticalAlignment(A);
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("SLOT_V_ALIGNMENT not supported for slot type '%s'"), *PanelSlot->GetClass()->GetName()));
		}
		else
		{
			return FMcpResponse::Failure(FString::Printf(TEXT("Unknown slot property: %s"), *PropertyName));
		}
	}
	// ── PROP_ properties ──────────────────────────────────────────────
	else if (PropertyName.StartsWith(TEXT("PROP_")))
	{
		if (PropertyName == TEXT("PROP_TEXT"))
		{
			if (UTextBlock* TB = Cast<UTextBlock>(Widget))
				TB->SetText(FText::FromString(PropertyValue));
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_TEXT requires TextBlock (or subclass), got %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_FONT_SIZE"))
		{
			if (UTextBlock* TB = Cast<UTextBlock>(Widget))
			{
				FSlateFontInfo F = TB->GetFont();
				F.Size = FCString::Atoi(*PropertyValue);
				TB->SetFont(F);
			}
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_FONT_SIZE requires TextBlock, got %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_FONT_TYPEFACE"))
		{
			if (UTextBlock* TB = Cast<UTextBlock>(Widget))
			{
				FSlateFontInfo F = TB->GetFont();
				F.TypefaceFontName = FName(*PropertyValue);
				TB->SetFont(F);
			}
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_FONT_TYPEFACE requires TextBlock, got %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_COLOR_HEX"))
		{
			FLinearColor Color;
			if (!ParseHexColor(PropertyValue, Color))
				return FMcpResponse::Failure(TEXT("Invalid hex color. Use: #RRGGBB or #RRGGBBAA"));
			if (UTextBlock* TB = Cast<UTextBlock>(Widget))
				TB->SetColorAndOpacity(FSlateColor(Color));
			else if (UImage* Img = Cast<UImage>(Widget))
				Img->SetColorAndOpacity(Color);
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_COLOR_HEX not supported for %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_JUSTIFICATION"))
		{
			ETextJustify::Type J = ETextJustify::Left;
			if (PropertyValue == TEXT("Center")) J = ETextJustify::Center;
			else if (PropertyValue == TEXT("Right"))  J = ETextJustify::Right;
			if (UTextBlock* TB = Cast<UTextBlock>(Widget))
				TB->SetJustification(J);
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_JUSTIFICATION requires TextBlock, got %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_AUTO_WRAP_TEXT"))
		{
			bool bWrap = (PropertyValue == TEXT("true") || PropertyValue == TEXT("1"));
			if (UTextBlock* TB = Cast<UTextBlock>(Widget))
				TB->SetAutoWrapText(bWrap);
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_AUTO_WRAP_TEXT requires TextBlock, got %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_MIN_DESIRED_WIDTH"))
		{
			float Val = FCString::Atof(*PropertyValue);
			if (USizeBox* SB = Cast<USizeBox>(Widget))
				SB->SetMinDesiredWidth(Val);
			else
			{
				// Reflection fallback (e.g. UGS_CommonButtonBase::MinDesiredWidth)
				FFloatProperty* Prop = FindFProperty<FFloatProperty>(Widget->GetClass(), TEXT("MinDesiredWidth"));
				if (Prop) Prop->SetPropertyValue_InContainer(Widget, Val);
				else return FMcpResponse::Failure(FString::Printf(
					TEXT("PROP_MIN_DESIRED_WIDTH: not a SizeBox and no MinDesiredWidth on %s"), *Widget->GetClass()->GetName()));
			}
		}
		else if (PropertyName == TEXT("PROP_MIN_DESIRED_HEIGHT"))
		{
			float Val = FCString::Atof(*PropertyValue);
			if (USizeBox* SB = Cast<USizeBox>(Widget))
				SB->SetMinDesiredHeight(Val);
			else if (UImage* Img = Cast<UImage>(Widget))
			{
				FSlateBrush Brush = Img->GetBrush();
				Brush.ImageSize.Y = Val;
				Img->SetBrush(Brush);
			}
			else
			{
				FFloatProperty* Prop = FindFProperty<FFloatProperty>(Widget->GetClass(), TEXT("MinDesiredHeight"));
				if (Prop) Prop->SetPropertyValue_InContainer(Widget, Val);
				else return FMcpResponse::Failure(FString::Printf(
					TEXT("PROP_MIN_DESIRED_HEIGHT: not a SizeBox/Image and no MinDesiredHeight on %s"), *Widget->GetClass()->GetName()));
			}
		}
		else if (PropertyName == TEXT("PROP_WIDTH_OVERRIDE"))
		{
			if (USizeBox* SB = Cast<USizeBox>(Widget))
				SB->SetWidthOverride(FCString::Atof(*PropertyValue));
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_WIDTH_OVERRIDE requires SizeBox, got %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_HEIGHT_OVERRIDE"))
		{
			if (USizeBox* SB = Cast<USizeBox>(Widget))
				SB->SetHeightOverride(FCString::Atof(*PropertyValue));
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_HEIGHT_OVERRIDE requires SizeBox, got %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_BRUSH_COLOR_HEX"))
		{
			FLinearColor Color;
			if (!ParseHexColor(PropertyValue, Color))
				return FMcpResponse::Failure(TEXT("Invalid hex color"));
			if (UBorder* B = Cast<UBorder>(Widget))
				B->SetBrushColor(Color);
			else if (UImage* Img = Cast<UImage>(Widget))
				Img->SetColorAndOpacity(Color);
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_BRUSH_COLOR_HEX not supported for %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_BRUSH_ALPHA"))
		{
			float Alpha = FCString::Atof(*PropertyValue);
			if (UBorder* Brd = Cast<UBorder>(Widget))
			{
				FLinearColor C = Brd->GetBrushColor();
				C.A = Alpha;
				Brd->SetBrushColor(C);
			}
			else if (UImage* Img = Cast<UImage>(Widget))
				Img->SetOpacity(Alpha);
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_BRUSH_ALPHA not supported for %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_PADDING"))
		{
			FMargin Margin;
			if (!ParseMargin(PropertyValue, Margin))
				return FMcpResponse::Failure(TEXT("Invalid margin. Use: value | h,v | l,t,r,b"));
			if (UBorder* Brd = Cast<UBorder>(Widget))
				Brd->SetPadding(Margin);
			else
			{
				// Non-Border: apply to slot padding
				UPanelSlot* PS = Widget->Slot;
				if (!PS) return FMcpResponse::Failure(FString::Printf(
					TEXT("PROP_PADDING: no slot on widget '%s'"), *WidgetName));
				if (auto* VS1 = Cast<UVerticalBoxSlot>(PS))          VS1->SetPadding(Margin);
				else if (auto* HS1 = Cast<UHorizontalBoxSlot>(PS))  HS1->SetPadding(Margin);
				else if (auto* OS1 = Cast<UOverlaySlot>(PS))         OS1->SetPadding(Margin);
				else if (auto* WS1 = Cast<UWidgetSwitcherSlot>(PS)) WS1->SetPadding(Margin);
				else return FMcpResponse::Failure(FString::Printf(
					TEXT("PROP_PADDING not supported: widget type '%s', slot type '%s'"),
					*Widget->GetClass()->GetName(), *PS->GetClass()->GetName()));
			}
		}
		else if (PropertyName == TEXT("PROP_SIZE_X") || PropertyName == TEXT("PROP_SIZE_Y"))
		{
			if (USpacer* Sp = Cast<USpacer>(Widget))
			{
				FVector2D S = Sp->GetSize();
				float Val = FCString::Atof(*PropertyValue);
				if (PropertyName == TEXT("PROP_SIZE_X")) S.X = Val;
				else                                      S.Y = Val;
				Sp->SetSize(S);
			}
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_SIZE_X/Y requires Spacer, got %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_WIDTH") || PropertyName == TEXT("PROP_HEIGHT"))
		{
			if (USpacer* Sp = Cast<USpacer>(Widget))
			{
				FVector2D S = Sp->GetSize();
				float Val = FCString::Atof(*PropertyValue);
				if (PropertyName == TEXT("PROP_WIDTH")) S.X = Val;
				else                                     S.Y = Val;
				Sp->SetSize(S);
			}
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_WIDTH/HEIGHT requires Spacer, got %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_ACTIVE_WIDGET_INDEX"))
		{
			if (UWidgetSwitcher* WS = Cast<UWidgetSwitcher>(Widget))
				WS->SetActiveWidgetIndex(FCString::Atoi(*PropertyValue));
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_ACTIVE_WIDGET_INDEX requires WidgetSwitcher, got %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_WIDTH_OVERRIDE_ENABLED"))
		{
			if (USizeBox* SB = Cast<USizeBox>(Widget))
			{
				if (PropertyValue == TEXT("false") || PropertyValue == TEXT("0"))
					SB->ClearWidthOverride();
				// true: no-op — SetWidthOverride call enables automatically
			}
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_WIDTH_OVERRIDE_ENABLED requires SizeBox, got %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_HEIGHT_OVERRIDE_ENABLED"))
		{
			if (USizeBox* SB = Cast<USizeBox>(Widget))
			{
				if (PropertyValue == TEXT("false") || PropertyValue == TEXT("0"))
					SB->ClearHeightOverride();
			}
			else return FMcpResponse::Failure(FString::Printf(
				TEXT("PROP_HEIGHT_OVERRIDE_ENABLED requires SizeBox, got %s"), *Widget->GetClass()->GetName()));
		}
		else if (PropertyName == TEXT("PROP_PADDING_LEFT") || PropertyName == TEXT("PROP_PADDING_TOP") ||
		         PropertyName == TEXT("PROP_PADDING_RIGHT") || PropertyName == TEXT("PROP_PADDING_BOTTOM"))
		{
			float Val = FCString::Atof(*PropertyValue);
			if (UBorder* Brd = Cast<UBorder>(Widget))
			{
				// Content padding — read-modify-write
				FMargin P = Brd->GetPadding();
				if (PropertyName == TEXT("PROP_PADDING_LEFT"))        P.Left   = Val;
				else if (PropertyName == TEXT("PROP_PADDING_TOP"))    P.Top    = Val;
				else if (PropertyName == TEXT("PROP_PADDING_RIGHT"))  P.Right  = Val;
				else if (PropertyName == TEXT("PROP_PADDING_BOTTOM")) P.Bottom = Val;
				Brd->SetPadding(P);
			}
			else
			{
				// Slot padding — read-modify-write
				UPanelSlot* PS = Widget->Slot;
				if (!PS) return FMcpResponse::Failure(FString::Printf(
					TEXT("PROP_PADDING_*: no slot on widget '%s'"), *WidgetName));
				auto SetSide = [&](FMargin& M)
				{
					if (PropertyName == TEXT("PROP_PADDING_LEFT"))        M.Left   = Val;
					else if (PropertyName == TEXT("PROP_PADDING_TOP"))    M.Top    = Val;
					else if (PropertyName == TEXT("PROP_PADDING_RIGHT"))  M.Right  = Val;
					else if (PropertyName == TEXT("PROP_PADDING_BOTTOM")) M.Bottom = Val;
				};
				if (auto* VS2 = Cast<UVerticalBoxSlot>(PS))
				{
					FMargin M = VS2->GetPadding(); SetSide(M); VS2->SetPadding(M);
				}
				else if (auto* HS2 = Cast<UHorizontalBoxSlot>(PS))
				{
					FMargin M = HS2->GetPadding(); SetSide(M); HS2->SetPadding(M);
				}
				else if (auto* OS2 = Cast<UOverlaySlot>(PS))
				{
					FMargin M = OS2->GetPadding(); SetSide(M); OS2->SetPadding(M);
				}
				else if (auto* WS2 = Cast<UWidgetSwitcherSlot>(PS))
				{
					FMargin M = WS2->GetPadding(); SetSide(M); WS2->SetPadding(M);
				}
				else return FMcpResponse::Failure(FString::Printf(
					TEXT("PROP_PADDING_*: slot type '%s' not supported"), *PS->GetClass()->GetName()));
			}
		}
		else if (PropertyName == TEXT("PROP_BUTTON_TEXT"))
		{
			FTextProperty* Prop = FindFProperty<FTextProperty>(Widget->GetClass(), TEXT("ButtonText"));
			if (!Prop) return FMcpResponse::Failure(FString::Printf(
				TEXT("ButtonText property not found on %s"), *Widget->GetClass()->GetName()));
			Prop->SetPropertyValue_InContainer(Widget, FText::FromString(PropertyValue));
		}
		else if (PropertyName == TEXT("PROP_IS_TOGGLEABLE"))
		{
			bool bVal = (PropertyValue == TEXT("true") || PropertyValue == TEXT("1"));
			FBoolProperty* Prop = FindFProperty<FBoolProperty>(Widget->GetClass(), TEXT("bToggleable"));
			if (!Prop) return FMcpResponse::Failure(FString::Printf(
				TEXT("bToggleable not found on %s"), *Widget->GetClass()->GetName()));
			Prop->SetPropertyValue_InContainer(Widget, bVal);
		}
		else if (PropertyName == TEXT("PROP_ACCENT_COLOR_HEX"))
		{
			FLinearColor Color;
			if (!ParseHexColor(PropertyValue, Color))
				return FMcpResponse::Failure(TEXT("Invalid hex color"));
			FStructProperty* Prop = FindFProperty<FStructProperty>(Widget->GetClass(), TEXT("AccentColor"));
			if (!Prop) return FMcpResponse::Failure(FString::Printf(
				TEXT("AccentColor not found on %s"), *Widget->GetClass()->GetName()));
			*Prop->ContainerPtrToValuePtr<FLinearColor>(Widget) = Color;
		}
		else if (PropertyName == TEXT("PROP_SELECTED_BACKGROUND_ALPHA"))
		{
			FFloatProperty* Prop = FindFProperty<FFloatProperty>(Widget->GetClass(), TEXT("SelectedBackgroundAlpha"));
			if (!Prop) return FMcpResponse::Failure(FString::Printf(
				TEXT("SelectedBackgroundAlpha not found on %s"), *Widget->GetClass()->GetName()));
			Prop->SetPropertyValue_InContainer(Widget, FCString::Atof(*PropertyValue));
		}
		else if (PropertyName == TEXT("PROP_IS_SELECTABLE"))
		{
			bool bSel = (PropertyValue == TEXT("true") || PropertyValue == TEXT("1"));
			// Use reflection to avoid CommonUI header dependency
			FBoolProperty* Prop = FindFProperty<FBoolProperty>(Widget->GetClass(), TEXT("bSelectable"));
			if (!Prop)
				return FMcpResponse::Failure(FString::Printf(
					TEXT("bSelectable not found on widget class '%s'"), *Widget->GetClass()->GetName()));
			Prop->SetPropertyValue_InContainer(Widget, bSel);
		}
		else
		{
			return FMcpResponse::Failure(FString::Printf(TEXT("Unknown property: %s"), *PropertyName));
		}
	}
	else
	{
		// Generic reflection fallback: treat PropertyName as raw UPROPERTY name
		FProperty* Prop = Widget->GetClass()->FindPropertyByName(FName(*PropertyName));
		if (!Prop)
		{
			return FMcpResponse::Failure(FString::Printf(
				TEXT("Property '%s' not found on %s. Use PROP_*/SLOT_* for built-in or exact UPROPERTY name."),
				*PropertyName, *Widget->GetClass()->GetName()));
		}

		void* ValuePtr = Prop->ContainerPtrToValuePtr<void>(Widget);
		const TCHAR* ImportResult = Prop->ImportText_Direct(*PropertyValue, ValuePtr, Widget, PPF_None);
		if (!ImportResult)
		{
			return FMcpResponse::Failure(FString::Printf(
				TEXT("Failed to parse value '%s' for property '%s' (type: %s)"),
				*PropertyValue, *PropertyName, *Prop->GetCPPType()));
		}

		// Notify property change for editor
		FPropertyChangedEvent ChangeEvent(Prop);
		Widget->PostEditChangeProperty(ChangeEvent);
	}

	if (SkipCompile != TEXT("true"))
	{
		FKismetEditorUtilities::CompileBlueprint(WBP);
		TArray<UPackage*> Packages = { WBP->GetOutermost() };
		FEditorFileUtils::PromptForCheckoutAndSave(Packages, false, false);
	}

	return FMcpResponse::Success(FString::Printf(
		TEXT("Set %s = '%s'  on  '%s'  in  %s"), *PropertyName, *PropertyValue, *WidgetName, *WbpPath));
}

FMcpResponse FUmgWidgetTreeTool::HandleDeleteWidget()
{
	if (WbpPath.IsEmpty() || WidgetName.IsEmpty())
		return FMcpResponse::Failure(TEXT("wbp_path and widget_name are required"));

	UWidgetBlueprint* WBP = LoadObject<UWidgetBlueprint>(nullptr, *WbpPath);
	if (!WBP || !WBP->WidgetTree)
		return FMcpResponse::Failure(FString::Printf(TEXT("Cannot load WBP: %s"), *WbpPath));

	UWidget* Widget = WBP->WidgetTree->FindWidget(FName(*WidgetName));
	if (!Widget)
		return FMcpResponse::Failure(FString::Printf(TEXT("Widget '%s' not found"), *WidgetName));

	// Cannot delete root widget
	if (Widget == WBP->WidgetTree->RootWidget)
		return FMcpResponse::Failure(TEXT("Cannot delete root widget"));

	WBP->WidgetTree->RemoveWidget(Widget);
	FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(WBP);

	if (SkipCompile != TEXT("true"))
	{
		FKismetEditorUtilities::CompileBlueprint(WBP);
		TArray<UPackage*> Packages = { WBP->GetOutermost() };
		FEditorFileUtils::PromptForCheckoutAndSave(Packages, false, false);
	}

	return FMcpResponse::Success(FString::Printf(TEXT("Deleted widget '%s' from %s"), *WidgetName, *WbpPath));
}

FMcpResponse FUmgWidgetTreeTool::HandleMoveWidget()
{
	if (WbpPath.IsEmpty() || WidgetName.IsEmpty())
		return FMcpResponse::Failure(TEXT("wbp_path and widget_name are required"));

	UWidgetBlueprint* WBP = LoadObject<UWidgetBlueprint>(nullptr, *WbpPath);
	if (!WBP || !WBP->WidgetTree)
		return FMcpResponse::Failure(FString::Printf(TEXT("Cannot load WBP: %s"), *WbpPath));

	UWidget* Widget = WBP->WidgetTree->FindWidget(FName(*WidgetName));
	if (!Widget)
		return FMcpResponse::Failure(FString::Printf(TEXT("Widget '%s' not found"), *WidgetName));

	// Find new parent (empty = root)
	UPanelWidget* NewParent = nullptr;
	if (ParentWidget.IsEmpty())
	{
		NewParent = Cast<UPanelWidget>(WBP->WidgetTree->RootWidget);
	}
	else
	{
		NewParent = Cast<UPanelWidget>(WBP->WidgetTree->FindWidget(FName(*ParentWidget)));
	}

	if (!NewParent)
		return FMcpResponse::Failure(FString::Printf(TEXT("Parent '%s' not found or not a PanelWidget"), *ParentWidget));

	// Remove from current parent
	UPanelWidget* OldParent = Widget->GetParent();
	if (OldParent)
	{
		OldParent->RemoveChild(Widget);
	}

	// Add to new parent
	UPanelSlot* Slot = NewParent->AddChild(Widget);
	if (!Slot)
		return FMcpResponse::Failure(FString::Printf(TEXT("Failed to add '%s' to '%s'"), *WidgetName, *ParentWidget));

	FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(WBP);

	if (SkipCompile != TEXT("true"))
	{
		FKismetEditorUtilities::CompileBlueprint(WBP);
		TArray<UPackage*> Packages = { WBP->GetOutermost() };
		FEditorFileUtils::PromptForCheckoutAndSave(Packages, false, false);
	}

	return FMcpResponse::Success(FString::Printf(TEXT("Moved '%s' to parent '%s' in %s"),
		*WidgetName, *NewParent->GetName(), *WbpPath));
}

FMcpResponse FUmgWidgetTreeTool::HandleGetWidgetProperty()
{
	if (WbpPath.IsEmpty() || WidgetName.IsEmpty())
		return FMcpResponse::Failure(TEXT("wbp_path and widget_name are required"));

	UWidgetBlueprint* WBP = LoadObject<UWidgetBlueprint>(nullptr, *WbpPath);
	if (!WBP || !WBP->WidgetTree)
		return FMcpResponse::Failure(FString::Printf(TEXT("Cannot load WBP: %s"), *WbpPath));

	UWidget* Widget = WBP->WidgetTree->FindWidget(FName(*WidgetName));
	if (!Widget)
		return FMcpResponse::Failure(FString::Printf(TEXT("Widget '%s' not found"), *WidgetName));

	// If specific property requested
	if (!PropertyName.IsEmpty())
	{
		FProperty* Prop = Widget->GetClass()->FindPropertyByName(FName(*PropertyName));
		if (!Prop)
			return FMcpResponse::Failure(FString::Printf(TEXT("Property '%s' not found on %s"),
				*PropertyName, *Widget->GetClass()->GetName()));

		FString ValueStr;
		const void* ValuePtr = Prop->ContainerPtrToValuePtr<void>(Widget);
		Prop->ExportText_Direct(ValueStr, ValuePtr, ValuePtr, Widget, PPF_None);

		return FMcpResponse::Success(FString::Printf(TEXT("%s = %s"), *PropertyName, *ValueStr));
	}

	// Return all non-default editable properties
	UObject* CDO = Widget->GetClass()->GetDefaultObject();
	FString Result = FString::Printf(TEXT("Properties of '%s' (%s):\n"), *WidgetName, *Widget->GetClass()->GetName());

	for (TFieldIterator<FProperty> PropIt(Widget->GetClass()); PropIt; ++PropIt)
	{
		FProperty* Prop = *PropIt;
		if (!Prop->HasAnyPropertyFlags(CPF_Edit)) continue;
		if (Prop->HasAnyPropertyFlags(CPF_Transient)) continue;

		const void* WidgetVal = Prop->ContainerPtrToValuePtr<void>(Widget);
		const void* CDOVal = Prop->ContainerPtrToValuePtr<void>(CDO);

		if (!Prop->Identical(WidgetVal, CDOVal))
		{
			FString ValueStr;
			Prop->ExportText_Direct(ValueStr, WidgetVal, WidgetVal, Widget, PPF_None);
			Result += FString::Printf(TEXT("  %s [%s] = %s\n"),
				*Prop->GetName(), *Prop->GetCPPType(), *ValueStr);
		}
	}

	return FMcpResponse::Success(Result);
}

FMcpResponse FUmgWidgetTreeTool::HandleGetWidgetSchema()
{
	if (WbpPath.IsEmpty() || WidgetName.IsEmpty())
		return FMcpResponse::Failure(TEXT("wbp_path and widget_name are required"));

	UWidgetBlueprint* WBP = LoadObject<UWidgetBlueprint>(nullptr, *WbpPath);
	if (!WBP || !WBP->WidgetTree)
		return FMcpResponse::Failure(FString::Printf(TEXT("Cannot load WBP: %s"), *WbpPath));

	UWidget* Widget = WBP->WidgetTree->FindWidget(FName(*WidgetName));
	if (!Widget)
		return FMcpResponse::Failure(FString::Printf(TEXT("Widget '%s' not found"), *WidgetName));

	FString Result = FString::Printf(TEXT("Schema for '%s' (%s):\n"), *WidgetName, *Widget->GetClass()->GetName());

	for (TFieldIterator<FProperty> PropIt(Widget->GetClass()); PropIt; ++PropIt)
	{
		FProperty* Prop = *PropIt;
		if (!Prop->HasAnyPropertyFlags(CPF_Edit)) continue;
		if (Prop->HasAnyPropertyFlags(CPF_Transient | CPF_EditorOnly)) continue;

		const FString& Category = Prop->GetMetaData(TEXT("Category"));
		Result += FString::Printf(TEXT("  %s [%s] — %s\n"),
			*Prop->GetName(), *Prop->GetCPPType(), *Category);
	}

	return FMcpResponse::Success(Result);
}

bool FUmgWidgetTreeTool::ParseHexColor(const FString& Hex, FLinearColor& OutColor)
{
	FString H = Hex.TrimChar('#');
	if (H.Len() == 6)
		H += TEXT("FF");
	if (H.Len() != 8)
		return false;

	FColor SRGBColor;
	SRGBColor.R = static_cast<uint8>(FCString::Strtoi(*H.Mid(0, 2), nullptr, 16));
	SRGBColor.G = static_cast<uint8>(FCString::Strtoi(*H.Mid(2, 2), nullptr, 16));
	SRGBColor.B = static_cast<uint8>(FCString::Strtoi(*H.Mid(4, 2), nullptr, 16));
	SRGBColor.A = static_cast<uint8>(FCString::Strtoi(*H.Mid(6, 2), nullptr, 16));

	OutColor = FLinearColor(SRGBColor); // sRGB → linear
	return true;
}

bool FUmgWidgetTreeTool::ParseMargin(const FString& Value, FMargin& OutMargin)
{
	TArray<FString> Parts;
	Value.ParseIntoArray(Parts, TEXT(","));
	for (FString& P : Parts) P.TrimStartAndEndInline();

	if (Parts.Num() == 1)
	{
		float V = FCString::Atof(*Parts[0]);
		OutMargin = FMargin(V);
		return true;
	}
	if (Parts.Num() == 2)
	{
		float H = FCString::Atof(*Parts[0]);
		float V = FCString::Atof(*Parts[1]);
		OutMargin = FMargin(H, V);
		return true;
	}
	if (Parts.Num() == 4)
	{
		OutMargin.Left   = FCString::Atof(*Parts[0]);
		OutMargin.Top    = FCString::Atof(*Parts[1]);
		OutMargin.Right  = FCString::Atof(*Parts[2]);
		OutMargin.Bottom = FCString::Atof(*Parts[3]);
		return true;
	}
	return false;
}

EHorizontalAlignment FUmgWidgetTreeTool::HAlignFromString(const FString& Value)
{
	if (Value == TEXT("Center")) return HAlign_Center;
	if (Value == TEXT("Right"))  return HAlign_Right;
	if (Value == TEXT("Fill"))   return HAlign_Fill;
	return HAlign_Left;
}

EVerticalAlignment FUmgWidgetTreeTool::VAlignFromString(const FString& Value)
{
	if (Value == TEXT("Center")) return VAlign_Center;
	if (Value == TEXT("Bottom")) return VAlign_Bottom;
	if (Value == TEXT("Fill"))   return VAlign_Fill;
	return VAlign_Top;
}
