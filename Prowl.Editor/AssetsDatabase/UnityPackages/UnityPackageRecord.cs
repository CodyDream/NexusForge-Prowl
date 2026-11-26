// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Editor.UnityPackages;

/// <summary>
/// One Unity package that has been imported into the open project. The source hash lets the editor
/// notice when the original archive has changed since it was imported.
/// </summary>
/// <param name="Name">The name the package was imported under, also its registry key.</param>
/// <param name="SourcePath">Absolute path of the source .unitypackage file.</param>
/// <param name="OutputFolder">Project-relative folder the package was extracted into.</param>
/// <param name="SourceHash">SHA-256 of the source file at import time.</param>
/// <param name="ImportedAt">When the package was imported.</param>
/// <param name="TrackedPaths">Project-relative files and folders written by the import.</param>
public sealed record UnityPackageRecord(
    string Name,
    string SourcePath,
    string OutputFolder,
    string SourceHash,
    DateTime ImportedAt,
    IReadOnlyList<string> TrackedPaths);
