#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using LumenvilLite.Models;
using LumenvilLite.Services;
using UnityEditor;
using UnityEngine;

namespace LumenvilLite.UI
{
    public class ProjectStepsWindow : EditorWindow
    {
        // Top-level kind dropdown.
        private static readonly string[] KindOptions = { "Preset", "Custom" };

        // Preset → Group dropdown.
        private static readonly string[] GroupOptions = { "Git", "Filesystem", "Notify" };

        // Group → Subset dropdowns (parallel to GroupOptions).
        private static readonly string[] GitSubsets =
            { "Fetch", "Pull", "Checkout", "Restore", "Reset", "Status", "Clean", "Tag" };
        private static readonly string[] FilesystemSubsets =
            { "Copy", "Move", "Delete", "Mkdir", "Zip" };
        private static readonly string[] NotifySubsets =
            { "Slack", "Discord", "HttpPost" };

        // Custom → Interpreter dropdown.
        private static readonly string[] InterpreterOptions = { "bash", "cmd", "pwsh", "direct" };

        private static readonly string[] PhaseTabs = { "Pre-build", "Post-build" };

        private LumenvilLiteClient _client;
        private CancellationTokenSource _cts;

        private string _projectName;
        private string _projectPath;
        private string _executeMethod;
        private List<StepDefinition> _preSteps = new();
        private List<StepDefinition> _postSteps = new();
        private int _phaseIndex; // 0 = pre, 1 = post

        private Vector2 _scroll;
        private bool _saving;
        private string _error;

        public Action StepsSaved;

        public static void Open(ProjectEntry entry, Action onSaved)
        {
            var window = GetWindow<ProjectStepsWindow>(utility: true, title: "Lumenvil Lite — Build Steps");
            window.minSize = new Vector2(660, 520);
            window._projectName = entry.name;
            window._projectPath = entry.projectPath;
            window._executeMethod = entry.executeMethod;
            window._preSteps = entry.preBuildSteps != null
                ? entry.preBuildSteps.Select(Clone).ToList()
                : new List<StepDefinition>();
            window._postSteps = entry.postBuildSteps != null
                ? entry.postBuildSteps.Select(Clone).ToList()
                : new List<StepDefinition>();
            window.StepsSaved = onSaved;
            window.Show();
        }

        private void OnEnable()
        {
            _client = new LumenvilLiteClient();
            _cts = new CancellationTokenSource();
        }

        private void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private List<StepDefinition> ActiveList =>
            _phaseIndex == 0 ? _preSteps : _postSteps;

        private void OnGUI()
        {
            EditorGUILayout.LabelField($"{_projectName} — Build steps", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Pre runs against projectPath before Unity spawns. Post runs after Unity exits and sees " +
                "LUMENVIL_OUTCOME / LUMENVIL_EXIT_CODE / LUMENVIL_PROJECT / LUMENVIL_TARGET / LUMENVIL_OUTPUT.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            _phaseIndex = GUILayout.Toolbar(_phaseIndex, PhaseTabs);
            EditorGUILayout.Space(4);

            DrawList(ActiveList);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Add step", GUILayout.Width(120)))
                {
                    ActiveList.Add(NewDefaultStep());
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(100)))
                {
                    Close();
                }
                using (new EditorGUI.DisabledScope(_saving))
                {
                    if (GUILayout.Button("Save", GUILayout.Width(120)))
                    {
                        SaveAsync().Forget();
                    }
                }
            }

            if (!string.IsNullOrEmpty(_error))
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
            }
        }

        private void DrawList(List<StepDefinition> steps)
        {
            if (steps.Count == 0)
            {
                EditorGUILayout.LabelField("No steps yet — click '+ Add step'.", EditorStyles.miniLabel);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < steps.Count; i++)
            {
                DrawStep(steps, i);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawStep(List<StepDefinition> steps, int i)
        {
            var step = steps[i];
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"Step {i + 1}", EditorStyles.boldLabel, GUILayout.Width(60));
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(i == 0))
                    {
                        if (GUILayout.Button("↑", GUILayout.Width(28))) Swap(steps, i, i - 1);
                    }
                    using (new EditorGUI.DisabledScope(i == steps.Count - 1))
                    {
                        if (GUILayout.Button("↓", GUILayout.Width(28))) Swap(steps, i, i + 1);
                    }
                    if (GUILayout.Button("✕", GUILayout.Width(28)))
                    {
                        steps.RemoveAt(i);
                        GUIUtility.ExitGUI();
                    }
                }

                var kindIndex = string.Equals(step.kind, "custom", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                var newKindIndex = EditorGUILayout.Popup("Kind", kindIndex, KindOptions);
                if (newKindIndex != kindIndex)
                {
                    step.kind = newKindIndex == 1 ? "custom" : "preset";
                    if (step.kind == "custom" && string.IsNullOrEmpty(step.interpreter))
                    {
                        step.interpreter = "bash";
                    }
                    if (step.kind == "preset" && string.IsNullOrEmpty(step.group))
                    {
                        step.group = "git";
                        step.subset = "Fetch";
                    }
                }

                if (step.kind == "custom")
                {
                    DrawCustomBody(step);
                }
                else
                {
                    DrawPresetBody(step);
                }

                EditorGUILayout.LabelField(Preview(step), EditorStyles.miniLabel);
            }
        }

        private static void DrawPresetBody(StepDefinition step)
        {
            var groupIndex = Mathf.Max(0, Array.FindIndex(GroupOptions,
                g => string.Equals(g, step.group, StringComparison.OrdinalIgnoreCase)));
            var newGroupIndex = EditorGUILayout.Popup("Group", groupIndex, GroupOptions);
            if (newGroupIndex != groupIndex || string.IsNullOrEmpty(step.group))
            {
                step.group = GroupOptions[newGroupIndex].ToLowerInvariant();
                step.subset = SubsetsFor(step.group)[0];
                step.args = string.Empty;
            }

            var subsets = SubsetsFor(step.group);
            var subsetIndex = Mathf.Max(0, Array.FindIndex(subsets,
                s => string.Equals(s, step.subset, StringComparison.OrdinalIgnoreCase)));
            var newSubsetIndex = EditorGUILayout.Popup("Subset", subsetIndex, subsets);
            step.subset = subsets[newSubsetIndex];

            step.args = EditorGUILayout.TextField(
                new GUIContent("Args", ArgsTooltip(step.group, step.subset)),
                step.args ?? string.Empty);
        }

        private static void DrawCustomBody(StepDefinition step)
        {
            var interpIndex = Mathf.Max(0, Array.FindIndex(InterpreterOptions,
                s => string.Equals(s, step.interpreter, StringComparison.OrdinalIgnoreCase)));
            var newInterpIndex = EditorGUILayout.Popup(
                new GUIContent("Interpreter",
                    "bash: Git for Windows bash.exe\ncmd: cmd.exe /c ...\npwsh: PowerShell\ndirect: split first token as exe (no shell)"),
                interpIndex, InterpreterOptions);
            step.interpreter = InterpreterOptions[newInterpIndex];

            step.command = EditorGUILayout.TextField(
                new GUIContent("Command", "Whole command line, passed to the chosen interpreter."),
                step.command ?? string.Empty);
        }

        private static string[] SubsetsFor(string group) => (group ?? string.Empty).ToLowerInvariant() switch
        {
            "git"        => GitSubsets,
            "filesystem" => FilesystemSubsets,
            "notify"     => NotifySubsets,
            _            => GitSubsets
        };

        private static string ArgsTooltip(string group, string subset)
        {
            switch ((group ?? string.Empty).ToLowerInvariant())
            {
                case "git": return (subset ?? string.Empty).ToLowerInvariant() switch
                {
                    "fetch"    => "Optional remote, e.g. 'origin'",
                    "pull"     => "Optional remote/branch, e.g. 'origin dev'",
                    "checkout" => "Branch/ref required, e.g. 'dev'",
                    "restore"  => "Pathspec, defaults to '.'",
                    "reset"    => "Defaults to '--hard'",
                    "clean"    => "Defaults to '-fd'",
                    "tag"      => "Tag name + optional flags, e.g. '-a v1.0 -m \"release\"'",
                    _          => "Optional extra arguments"
                };
                case "filesystem": return (subset ?? string.Empty).ToLowerInvariant() switch
                {
                    "copy"   => "<src> <dst> — directories copy recursively",
                    "move"   => "<src> <dst>",
                    "delete" => "<path> [<path>...] — files or directories",
                    "mkdir"  => "<path> [<path>...]",
                    "zip"    => "<src-dir> <dst-zip>",
                    _        => string.Empty
                };
                case "notify": return (subset ?? string.Empty).ToLowerInvariant() switch
                {
                    "slack"    => "<webhook-url>",
                    "discord"  => "<webhook-url>",
                    "httppost" => "<url> [<json-body>]",
                    _          => string.Empty
                };
                default: return string.Empty;
            }
        }

        private static StepDefinition NewDefaultStep() => new StepDefinition
        {
            kind = "preset",
            group = "git",
            subset = "Fetch",
            args = string.Empty
        };

        private static void Swap(List<StepDefinition> list, int a, int b)
        {
            (list[a], list[b]) = (list[b], list[a]);
        }

        private static string Preview(StepDefinition step)
        {
            if (string.Equals(step.kind, "custom", StringComparison.OrdinalIgnoreCase))
            {
                var interp = string.IsNullOrEmpty(step.interpreter) ? "bash" : step.interpreter;
                var cmd = string.IsNullOrEmpty(step.command) ? "(empty)" : step.command;
                return interp == "direct" ? cmd : $"{interp} {cmd}";
            }

            var group = (step.group ?? "git").ToLowerInvariant();
            var subset = (step.subset ?? string.Empty).ToLowerInvariant();
            var args = (step.args ?? string.Empty).Trim();
            return group switch
            {
                "git"        => string.IsNullOrEmpty(args) ? $"git {subset}" : $"git {subset} {args}",
                "filesystem" => $"filesystem {subset} {args}",
                "notify"     => $"notify {subset} {args}",
                _            => $"{group} {subset} {args}"
            };
        }

        private async UniTaskVoid SaveAsync()
        {
            _saving = true;
            _error = null;
            Repaint();
            try
            {
                var entry = new ProjectEntry
                {
                    name = _projectName,
                    projectPath = _projectPath,
                    executeMethod = _executeMethod,
                    preBuildSteps = _preSteps.ToArray(),
                    postBuildSteps = _postSteps.ToArray()
                };
                await _client.UpdateProjectAsync(_projectName, entry, _cts.Token);
                StepsSaved?.Invoke();
                Close();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _error = ex.Message;
                Repaint();
            }
            finally
            {
                _saving = false;
            }
        }

        private static StepDefinition Clone(StepDefinition s) => new StepDefinition
        {
            kind = s.kind,
            group = s.group,
            subset = s.subset,
            args = s.args,
            interpreter = s.interpreter,
            command = s.command
        };
    }
}
#endif
