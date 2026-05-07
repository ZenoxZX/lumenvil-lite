#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Intentionally kept in the global namespace so Unity's -executeMethod
// parser can find this with a bare 'LumenvilLiteBuilder.Build' argument.
// Unity resolves Class.Method only inside the namespace declared in the
// arg string; passing the dotted namespace path errors out with
// "executeMethod class '<name>' could not be found", which is why this
// file does not wrap its type in a namespace.
//
// If you copy this file into your project as a template, feel free to
// wrap it in your own namespace — just remember to register the project
// with the matching 'YourNamespace.YourClass.YourMethod' executeMethod
// string in Manage Projects.
public static class LumenvilLiteBuilder
{
    public static void Build()
    {
        var args = Environment.GetCommandLineArgs();
        string GetArg(string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        var targetArg  = GetArg("-lumenvilTarget");
        var backendArg = GetArg("-lumenvilBackend");
        var output     = GetArg("-lumenvilOutput");
        var defines    = GetArg("-lumenvilDefines");

        if (string.IsNullOrEmpty(targetArg) || string.IsNullOrEmpty(output))
        {
            Debug.LogError($"[LumenvilLite] Missing required args. target='{targetArg}' output='{output}'");
            EditorApplication.Exit(2);
            return;
        }

        if (!Enum.TryParse(targetArg, ignoreCase: false, out BuildTarget buildTarget))
        {
            Debug.LogError($"[LumenvilLite] Unknown build target '{targetArg}'.");
            EditorApplication.Exit(3);
            return;
        }

        var group = BuildPipeline.GetBuildTargetGroup(buildTarget);

        if (!string.IsNullOrEmpty(backendArg))
        {
            var backend = string.Equals(backendArg, "Il2cpp", StringComparison.OrdinalIgnoreCase)
                ? ScriptingImplementation.IL2CPP
                : ScriptingImplementation.Mono2x;
            PlayerSettings.SetScriptingBackend(group, backend);
        }

        if (!string.IsNullOrEmpty(defines))
        {
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, defines);
        }

        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("[LumenvilLite] No scenes enabled in Build Settings — aborting.");
            EditorApplication.Exit(4);
            return;
        }

        Directory.CreateDirectory(output);
        var locationPath = Path.Combine(output, GetExecutableName(buildTarget));

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = locationPath,
            target = buildTarget,
            targetGroup = group,
            options = BuildOptions.None
        };

        Debug.Log($"[LumenvilLite] Building target={buildTarget} backend={backendArg} " +
                  $"defines='{defines}' scenes={scenes.Length} output='{locationPath}'");

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        Debug.Log($"[LumenvilLite] Build result={summary.result} " +
                  $"size={summary.totalSize} duration={summary.totalTime} errors={summary.totalErrors}");

        // BuildPipeline.BuildPlayer does not propagate failure to the
        // process exit code on its own, so we have to nudge Unity into
        // a non-zero exit when something went wrong.
        if (summary.result != BuildResult.Succeeded)
        {
            EditorApplication.Exit(1);
        }
    }

    private static string GetExecutableName(BuildTarget target)
    {
        switch (target)
        {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                return "Game.exe";
            case BuildTarget.StandaloneOSX:
                return "Game.app";
            case BuildTarget.StandaloneLinux64:
                return "Game";
            case BuildTarget.Android:
                return "Game.apk";
            case BuildTarget.WebGL:
                return string.Empty;
            default:
                return "Game";
        }
    }
}
#endif
