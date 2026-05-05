#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LumenvilLite.Editor.Build
{
    /// <summary>
    /// Default build entry point used by the Lumenvil Lite server when a
    /// project entry is registered with an empty <c>executeMethod</c>.
    ///
    /// Reads the <c>-lumenvil*</c> CLI args injected by the server,
    /// applies the requested scripting backend and defines, then runs
    /// <see cref="BuildPipeline.BuildPlayer"/> with the scenes that are
    /// enabled in <see cref="EditorBuildSettings"/>.
    ///
    /// You can also point your project's <c>executeMethod</c> straight at
    /// <c>LumenvilLite.Editor.Build.LumenvilLiteBuilder.Build</c> to use
    /// this method explicitly, or copy this file into your own project
    /// and tweak it (Addressables build, version stamping, custom output
    /// name, etc.) — that's why it lives here as a template.
    /// </summary>
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

            // Backend (Il2cpp / Mono). Server only sends StandaloneWindows64
            // for now, but the switch is harmless on other targets.
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
                    return string.Empty; // WebGL builds an output folder, no exe.
                default:
                    return "Game";
            }
        }
    }
}
#endif
