using System.Collections.Generic;

namespace SheetMan.CodeGeneration
{
    /// <summary>Everything the Unreal templates need, worked out in advance.</summary>
    internal sealed class UnrealFileView
    {
        /// <summary>Name of the accessor class, which also names the header and the .cpp.</summary>
        public string AccessorName { get; set; }

        /// <summary>
        /// The module's export macro, `MODULENAME_API`.
        ///
        /// Every public type carries it, or the module links but nothing outside it can
        /// reach the generated types.
        /// </summary>
        public string ApiMacro { get; set; }

        public IReadOnlyList<UnrealEnumView> Enums { get; set; }
        public IReadOnlyList<UnrealTableView> Tables { get; set; }
        public UnrealAccessorView Accessor { get; set; }
    }

    internal sealed class UnrealEnumView
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }
        public IReadOnlyList<UnrealEnumLabelView> Labels { get; set; }
    }

    internal sealed class UnrealEnumLabelView
    {
        public string Name { get; set; }
        public string Value { get; set; }

        /// <summary>What the editor shows, which is the label as the sheet spelled it.</summary>
        public string DisplayName { get; set; }

        public IReadOnlyList<string> Comment { get; set; }
    }

    internal sealed class UnrealTableView
    {
        public string RawName { get; set; }
        public string RecordName { get; set; }
        public string TableName { get; set; }
        public string Location { get; set; }
        public IReadOnlyList<string> Comment { get; set; }

        /// <summary>Name of the primary index member.</summary>
        public string IndexField { get; set; }

        public IReadOnlyList<UnrealFieldView> Fields { get; set; }
    }

    internal sealed class UnrealFieldView
    {
        public IReadOnlyList<string> Comment { get; set; }

        public string Name { get; set; }

        /// <summary>The member declaration, including its initializer.</summary>
        public string Declaration { get; set; }

        /// <summary>
        /// Whether the member carries a UPROPERTY.
        ///
        /// Almost always yes. A double does not, because UE4's header tool rejects the type
        /// outright and the generated module is meant to build on both UE4 and UE5.
        /// </summary>
        public bool BlueprintVisible { get; set; }

        /// <summary>Why it does not, written into the generated code beside the member.</summary>
        public string NotVisibleBecause { get; set; }

        /// <summary>
        /// Which read shape applies: `var_array`, `serial_ref`, `serial`, `scalar_ref` or
        /// `scalar`.
        /// </summary>
        public string Kind { get; set; }

        public int ElementCount { get; set; }

        /// <summary>
        /// Type of the temporary a value is read into.
        ///
        /// The shared C++ reader fills an out parameter rather than returning, so every
        /// read is a small block: declare, read, convert. That also gives the conversions
        /// - UTF-8 to FString, ticks to FDateTime - somewhere to happen.
        /// </summary>
        public string TempType { get; set; }

        /// <summary>`read` for everything but an enum, which has its own overload.</summary>
        public string ReadCall { get; set; }

        /// <summary>The expression turning the temporary into what the member holds.</summary>
        public string FromTemp { get; set; }
    }

    internal sealed class UnrealAccessorView
    {
        public string FileExtension { get; set; }
        public IReadOnlyList<UnrealTableSlotView> Tables { get; set; }
    }

    internal sealed class UnrealTableSlotView
    {
        public string Name { get; set; }
        public string TableName { get; set; }
        public string DataFileName { get; set; }
    }
}
