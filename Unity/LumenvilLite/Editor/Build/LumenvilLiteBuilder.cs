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

        bool HasFlag(string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        var targetArg  = GetArg("-lumenvilTarget");
        var backendArg = GetArg("-lumenvilBackend");
        var output     = GetArg("-lumenvilOutput");
        var defines    = GetArg("-lumenvilDefines");

        var development         = HasFlag("-lumenvilDevelopment");
        var autoConnectProfiler = HasFlag("-lumenvilAutoConnectProfiler");
        var deepProfiling       = HasFlag("-lumenvilDeepProfiling");
        var scriptDebugging     = HasFlag("-lumenvilScriptDebugging");

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

        // Surface PlayerSettings.bundleVersion (Application.version) to the
        // Lumenvil server so post-build steps can use it. Written before
        // BuildPipeline.BuildPlayer because that call may rewrite the output
        // directory; the server picks this up after Unity exits.
        try
        {
            File.WriteAllText(Path.Combine(output, ".lumenvil-version.txt"),
                Application.version ?? string.Empty);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LumenvilLite] Could not write .lumenvil-version.txt: {e.Message}");
        }

        // Development is the master switch — the other three flags do nothing
        // (or worse, get baked into a release build) without it. The server
        // already only sends profiler/debug args alongside Development, but
        // we re-check here so a stale CLI invocation can't slip through.
        var buildOptions = BuildOptions.None;
        if (development)
        {
            buildOptions |= BuildOptions.Development;
            if (autoConnectProfiler) buildOptions |= BuildOptions.ConnectWithProfiler;
            if (deepProfiling)       buildOptions |= BuildOptions.EnableDeepProfilingSupport;
            if (scriptDebugging)     buildOptions |= BuildOptions.AllowDebugging;
        }
        else if (autoConnectProfiler || deepProfiling || scriptDebugging)
        {
            Debug.LogWarning("[LumenvilLite] Profiler / deep-profiling / script-debugging " +
                             "ignored because -lumenvilDevelopment was not set.");
        }

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = locationPath,
            target = buildTarget,
            targetGroup = group,
            options = buildOptions
        };

        Debug.Log($"[LumenvilLite] Building target={buildTarget} backend={backendArg} " +
                  $"defines='{defines}' options={buildOptions} scenes={scenes.Length} output='{locationPath}'");

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
        // Use the project's PlayerSettings.productName so the produced
        // executable matches the launch option configured on Steamworks
        // (and any other launcher). Falls back to "Game" only if the
        // product name is empty or sanitizes to nothing.
        var baseName = SanitizeFileName(PlayerSettings.productName);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Game";

        switch (target)
        {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                return baseName + ".exe";
            case BuildTarget.StandaloneOSX:
                return baseName + ".app";
            case BuildTarget.StandaloneLinux64:
                return baseName;
            case BuildTarget.Android:
                return baseName + ".apk";
            case BuildTarget.WebGL:
                return string.Empty;
            default:
                return baseName;
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
        }
        return new string(chars).Trim();
    }
}
#endif
