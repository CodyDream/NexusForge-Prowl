using System.Collections.Generic;

using Prowl.Runtime;

namespace Prowl.Editor.Importers;

/// <summary>
/// The object produced when a .unitypackage is imported. It is deliberately small: the package's
/// contents become ordinary Prowl assets of their own, and this is only the manifest that says what
/// was extracted and where. Keeping it as an Asset gives the package entry a stable identity in the
/// database (and a cache file) instead of producing an importer that claims success with nothing to
/// show for it.
/// </summary>
public sealed class UnityImportReport : Asset
{
    /// <summary>Absolute path of the .unitypackage this was read from.</summary>
    public string SourcePath = "";

    /// <summary>Folder under Assets/ the package was extracted into (relative path, no trailing slash).</summary>
    public string OutputFolder = "";

    /// <summary>Number of asset entries (excluding .meta files) in the package.</summary>
    public int EntryCount;

    /// <summary>Raw files handed to the existing texture/model/audio importers.</summary>
    public int RawImports;

    /// <summary>Unity materials converted to Lit materials.</summary>
    public int Materials;

    /// <summary>Unity ScriptableObjects converted to minimal Prowl assets.</summary>
    public int ScriptableObjects;

    /// <summary>Unity .meta files archived for reference (never used as Prowl metadata).</summary>
    public int MetaFiles;

    /// <summary>Entries that were not converted (binary assets, unsupported types, unsafe paths).</summary>
    public int Skipped;

    /// <summary>Prefabs left as source pending the Echo-tree conversion.</summary>
    public int SkippedPrefabs;

    /// <summary>Scenes left as source pending the Echo-tree conversion.</summary>
    public int SkippedScenes;

    public UnityImportReport() : base("Unity Import Report") { }
}

/// <summary>
/// A deliberately loose representation of a Unity ScriptableObject. Unity's .asset payloads are
/// arbitrary serialised fields whose types are defined by scripts we do not import, so the useful
/// thing to preserve is the raw scalar/ref data, not a typed copy of a class that does not exist in
/// Prowl. Concrete subclasses can be introduced later for the few types worth translating properly.
/// </summary>
public sealed class UnityScriptableObjectAsset : Asset
{
    /// <summary>Unity class ID of the document the values were read from (114 for MonoBehaviour).</summary>
    public long ClassId;

    /// <summary>Unity file ID of that document.</summary>
    public long FileId;

    /// <summary>GUID in the m_Script reference, which identifies the Unity script type.</summary>
    public string ScriptGuid = "";

    /// <summary>File ID of the referenced m_Script.</summary>
    public long ScriptFileId;

    /// <summary>Unity type code of the referenced m_Script.</summary>
    public int ScriptType;

    /// <summary>Top-level scalar fields, keyed by Unity field name.</summary>
    public Dictionary<string, string> Values = [];

    /// <summary>Top-level object references, formatted as "guid/fileId/type".</summary>
    public Dictionary<string, string> References = [];

    public UnityScriptableObjectAsset() : base("Unity ScriptableObject") { }
}
