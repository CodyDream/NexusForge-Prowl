// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.GUI;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;

namespace Prowl.Editor.PolyHaven;

/// <summary>
/// Persists the name put in the Poly Haven User-Agent. Registered as project settings so it is saved with the
/// project by the usual editor settings mechanism, but drawn in the Poly Haven browser rather than as another
/// Project Settings page, where it belongs next to the downloads it identifies.
/// </summary>
[ProjectSettings("Poly Haven", EditorIcons.Globe, order: 150, exportToBuild: false)]
public sealed class PolyHavenUserAgentSettings : ProjectSettingsBase
{
    /// <summary>The project/application name sent as the User-Agent comment.</summary>
    public string ApplicationName = PolyHavenUserAgent.DefaultName;

    public override bool DrawInProjectSettingsPanel => false;

    public override void ResetToDefaults() => ApplicationName = PolyHavenUserAgent.DefaultName;

    public override void OnGUI(Paper paper, float width)
        => EditorGUI.SettingsTextField(paper, "ph_ua_name", Loc.Get("polyhaven.user_agent"),
            ApplicationName, v => ApplicationName = v);
}
