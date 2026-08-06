// Compiles and links the generated updater, and calls into it.
//
// The calls are what makes this a link check rather than a compile check: a header that
// declares what the .cpp does not define would pass a compile and fail a project. Nothing
// here reaches the network - the updater's own tests do that, in C#, against a real server -
// so what is proven here is that the engine's API is what the updater thinks it is.

#include "CoreMinimal.h"
#include "RequiredProgramMainCPPInclude.h"

#include "SheetManUpdater.h"

DEFINE_LOG_CATEGORY_STATIC(LogSheetManUpdaterCheck, Log, All);

IMPLEMENT_APPLICATION(SheetManUpdaterCheck, "SheetManUpdaterCheck");

INT32_MAIN_INT32_ARGC_TCHAR_ARGV()
{
	GEngineLoop.PreInit(ArgC, ArgV);

	// The manifest reader, over the shape the exporter writes.
	const FString Manifest = TEXT(
		"{\"MasterHash\":\"abc\",\"Items\":[{\"Name\":\"Item.table\",\"Size\":232,\"Hash\":\"f76019ff\"}]}");

	TArray<FSheetManManifestEntry> Entries;
	FSheetManUpdater::ParseManifest(Manifest, Entries);

	if (Entries.Num() != 1 || Entries[0].Name != TEXT("Item.table") || Entries[0].Size != 232)
	{
		UE_LOG(LogSheetManUpdaterCheck, Error, TEXT("The manifest parser did not read what it was given."));
		FEngineLoop::AppExit();
		return 1;
	}

	// The hash, against a value this program can check without a server.
	TArray<uint8> Bytes;
	Bytes.Append(reinterpret_cast<const uint8*>("abc"), 3);

	if (FSheetManUpdater::HashOf(Bytes) != TEXT("900150983cd24fb0d6963f7d28e17f72"))
	{
		UE_LOG(LogSheetManUpdaterCheck, Error, TEXT("MD5 of \"abc\" was not what MD5 of \"abc\" is."));
		FEngineLoop::AppExit();
		return 1;
	}

	// And the entry point, so the whole state machine is linked rather than only the parts
	// the two checks above reach. The URL is unreachable on purpose: this returns
	// immediately and the program exits before the request could finish.
	if (FSheetManUpdater::DefaultCacheDirectory().IsEmpty())
	{
		UE_LOG(LogSheetManUpdaterCheck, Error, TEXT("There is no default cache directory."));
		FEngineLoop::AppExit();
		return 1;
	}

	UE_LOG(LogSheetManUpdaterCheck, Display, TEXT("The updater compiles, links and runs."));

	FEngineLoop::AppExit();
	return 0;
}
