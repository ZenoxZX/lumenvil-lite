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
        private static readonly string[] PresetOptions =
        {
            "Fetch", "Pull", "Checkout", "Restore", "Reset", "Status", "Clean"
        };
        private static readonly string[] KindOptions = { "Preset", "Custom" };

        private LumenvilLiteClient _client;
        private CancellationTokenSource _cts;

        private string _projectName;
        private string _projectPath;
        private string _executeMethod;
        private List<GitStep> _steps = new();

        private Vector2 _scroll;
        private bool _saving;
        private string _error;

        public Action StepsSaved;

        public static void Open(ProjectEntry entry, Action onSaved)
        {
            var window = GetWindow<ProjectStepsWindow>(utility: true, title: "Lumenvil Lite — Pre-build Steps");
            window.minSize = new Vector2(620, 480);
            window._projectName = entry.name;
            window._projectPath = entry.projectPath;
            window._executeMethod = entry.executeMethod;
            window._steps = entry.preBuildSteps != null
                ? entry.preBuildSteps.Select(Clone).ToList()
                : new List<GitStep>();
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

        private void OnGUI()
        {
            EditorGUILayout.LabelField($"{_projectName} — Pre-build steps", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Steps run in order against the project path before each build.", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            // Scrollable step list expands to fill the window.
            DrawList();

            // Action footer always pinned to the bottom.
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Add step", GUILayout.Width(120)))
                {
                    _steps.Add(new GitStep { kind = "preset", preset = "Fetch", args = string.Empty });
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

        private void DrawList()
        {
            if (_steps.Count == 0)
            {
                EditorGUILayout.LabelField("No steps yet — click '+ Add step' to add one.",
                    EditorStyles.miniLabel);
                return;
            }

            // Scroll view fills the remaining vertical space so the action
            // row at the bottom stays visible no matter how many steps exist.
            _scroll = EditorGUILayout.BeginScrollView(_scroll,
                GUILayout.ExpandHeight(true));
            for (int i = 0; i < _steps.Count; i++)
            {
                DrawStep(i);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawStep(int i)
        {
            var step = _steps[i];
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Header row: index + reorder/remove buttons together.
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"Step {i + 1}", EditorStyles.boldLabel, GUILayout.Width(60));
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(i == 0))
                    {
                        if (GUILayout.Button("↑", GUILayout.Width(28))) Swap(i, i - 1);
                    }
                    using (new EditorGUI.DisabledScope(i == _steps.Count - 1))
                    {
                        if (GUILayout.Button("↓", GUILayout.Width(28))) Swap(i, i + 1);
                    }
                    if (GUILayout.Button("✕", GUILayout.Width(28)))
                    {
                        _steps.RemoveAt(i);
                        GUIUtility.ExitGUI();
                    }
                }

                // Body fields.
                var kindIndex = string.Equals(step.kind, "custom", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                var newKindIndex = EditorGUILayout.Popup("Kind", kindIndex, KindOptions);
                if (newKindIndex != kindIndex)
                {
                    step.kind = newKindIndex == 1 ? "custom" : "preset";
                }

                if (step.kind == "custom")
                {
                    step.customCommand = EditorGUILayout.TextField(
                        new GUIContent("Command", "Whatever follows 'git ' — e.g. 'stash pop', 'submodule update --init'"),
                        step.customCommand ?? string.Empty);
                }
                else
                {
                    var presetIndex = Mathf.Max(0, Array.FindIndex(PresetOptions,
                        p => string.Equals(p, step.preset, StringComparison.OrdinalIgnoreCase)));
                    var newPresetIndex = EditorGUILayout.Popup("Preset", presetIndex, PresetOptions);
                    step.preset = PresetOptions[newPresetIndex];

                    step.args = EditorGUILayout.TextField(
                        new GUIContent("Args", PresetTooltip(step.preset)),
                        step.args ?? string.Empty);
                }

                EditorGUILayout.LabelField($"git {Preview(step)}", EditorStyles.miniLabel);
            }
        }

        private void Swap(int a, int b)
        {
            (_steps[a], _steps[b]) = (_steps[b], _steps[a]);
        }

        private static string PresetTooltip(string preset) => preset?.ToLowerInvariant() switch
        {
            "fetch"    => "Optional remote, e.g. 'origin'",
            "pull"     => "Optional remote/branch, e.g. 'origin dev'",
            "checkout" => "Branch/ref required, e.g. 'dev'",
            "restore"  => "Pathspec, defaults to '.'",
            "reset"    => "Defaults to '--hard'",
            "clean"    => "Defaults to '-fd'",
            _          => "Optional extra arguments"
        };

        private static string Preview(GitStep step)
        {
            if (string.Equals(step.kind, "custom", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(step.customCommand) ? "(empty)" : step.customCommand.Trim();
            }
            var preset = (step.preset ?? string.Empty).ToLowerInvariant();
            var head = preset switch
            {
                "fetch"    => "fetch",
                "pull"     => "pull",
                "checkout" => "checkout",
                "restore"  => "restore",
                "reset"    => "reset",
                "status"   => "status",
                "clean"    => "clean",
                _          => "status"
            };
            var args = (step.args ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(args))
            {
                if (preset == "restore") return "restore .";
                if (preset == "reset")   return "reset --hard";
                if (preset == "clean")   return "clean -fd";
                return head;
            }
            return $"{head} {args}";
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
                    preBuildSteps = _steps.ToArray()
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

        private static GitStep Clone(GitStep s) => new GitStep
        {
            kind = s.kind,
            preset = s.preset,
            args = s.args,
            customCommand = s.customCommand
        };
    }
}
#endif
