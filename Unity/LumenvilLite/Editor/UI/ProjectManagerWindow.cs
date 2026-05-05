#if UNITY_EDITOR
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LumenvilLite.Models;
using LumenvilLite.Services;
using UnityEditor;
using UnityEngine;

namespace LumenvilLite.UI
{
    public class ProjectManagerWindow : EditorWindow
    {
        private LumenvilLiteClient _client;
        private CancellationTokenSource _cts;

        private ProjectEntry[] _projects = Array.Empty<ProjectEntry>();
        private bool _loading;
        private string _loadError;

        private string _newName = string.Empty;
        private string _newProjectPath = string.Empty;
        private string _newExecuteMethod = string.Empty;
        private string _addError;
        private bool _adding;

        // Edit mode: when set, the Add form acts as an Update form for the
        // entry whose original key is _editingOriginalName.
        private string _editingOriginalName;

        private Vector2 _scroll;

        public Action ProjectsChanged;

        public static void Open(Action onChanged)
        {
            var window = GetWindow<ProjectManagerWindow>(utility: true, title: "Lumenvil Lite — Projects");
            window.minSize = new Vector2(520, 360);
            window.ProjectsChanged = onChanged;
            window.Show();
        }

        private void OnEnable()
        {
            _client = new LumenvilLiteClient();
            _cts = new CancellationTokenSource();
            ReloadFireAndForget();
        }

        private void ReloadFireAndForget() => ReloadAsync().Forget();

        private void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private async UniTask ReloadAsync()
        {
            _loading = true;
            _loadError = null;
            Repaint();
            try
            {
                var response = await _client.GetProjectsAsync(_cts.Token);
                _projects = response?.projects ?? Array.Empty<ProjectEntry>();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _loadError = ex.Message;
                _projects = Array.Empty<ProjectEntry>();
            }
            finally
            {
                _loading = false;
                Repaint();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Registered build projects", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            DrawList();
            EditorGUILayout.Space(8);
            DrawAddForm();

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", GUILayout.Width(90)))
                {
                    ReloadAsync().Forget();
                }
                if (GUILayout.Button("Close", GUILayout.Width(90)))
                {
                    Close();
                }
            }
        }

        private void DrawList()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (_loading)
                {
                    EditorGUILayout.LabelField("Loading...");
                    return;
                }
                if (!string.IsNullOrEmpty(_loadError))
                {
                    EditorGUILayout.HelpBox($"Failed to load projects: {_loadError}", MessageType.Error);
                    return;
                }
                if (_projects.Length == 0)
                {
                    EditorGUILayout.LabelField("No projects yet — add one below.");
                    return;
                }

                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(150));
                foreach (var project in _projects)
                {
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.VerticalScope())
                        {
                            EditorGUILayout.LabelField(project.name, EditorStyles.boldLabel);
                            EditorGUILayout.LabelField(project.projectPath, EditorStyles.miniLabel);
                            var methodLabel = string.IsNullOrEmpty(project.executeMethod)
                                ? "executeMethod: (built-in builder)"
                                : $"executeMethod: {project.executeMethod}";
                            EditorGUILayout.LabelField(methodLabel, EditorStyles.miniLabel);
                        }
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(80)))
                        {
                            if (GUILayout.Button("Edit"))
                            {
                                BeginEdit(project);
                            }
                            if (GUILayout.Button("Remove"))
                            {
                                DeleteAsync(project.name).Forget();
                            }
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawAddForm()
        {
            var editing = !string.IsNullOrEmpty(_editingOriginalName);
            var heading = editing ? $"Edit '{_editingOriginalName}'" : "Add a project";
            EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _newName = EditorGUILayout.TextField("Name", _newName);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _newProjectPath = EditorGUILayout.TextField("Project path", _newProjectPath);
                    if (GUILayout.Button("Browse...", GUILayout.Width(80)))
                    {
                        var picked = EditorUtility.OpenFolderPanel("Select Unity project root", _newProjectPath, "");
                        if (!string.IsNullOrEmpty(picked))
                        {
                            _newProjectPath = picked;
                        }
                    }
                }

                _newExecuteMethod = EditorGUILayout.TextField(
                    new GUIContent("Execute method (optional)",
                        "Fully qualified static method, e.g. BuildScript.BuildFromLumenvil. " +
                        "Leave empty to use the built-in LumenvilLite.Editor.Build.LumenvilLiteBuilder.Build."),
                    _newExecuteMethod);
                EditorGUILayout.LabelField(
                    "Leave empty to use the built-in builder.",
                    EditorStyles.miniLabel);

                if (!string.IsNullOrEmpty(_addError))
                {
                    EditorGUILayout.HelpBox(_addError, MessageType.Error);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (editing && GUILayout.Button("Cancel", GUILayout.Width(90)))
                    {
                        ClearEdit();
                    }
                    using (new EditorGUI.DisabledScope(_adding))
                    {
                        if (GUILayout.Button(editing ? "Update" : "Add", GUILayout.Width(120)))
                        {
                            SubmitAsync().Forget();
                        }
                    }
                }
            }
        }

        private void BeginEdit(ProjectEntry entry)
        {
            _editingOriginalName = entry.name;
            _newName = entry.name ?? string.Empty;
            _newProjectPath = entry.projectPath ?? string.Empty;
            _newExecuteMethod = entry.executeMethod ?? string.Empty;
            _addError = null;
            Repaint();
        }

        private void ClearEdit()
        {
            _editingOriginalName = null;
            _newName = _newProjectPath = _newExecuteMethod = string.Empty;
            _addError = null;
            Repaint();
        }

        private async UniTaskVoid SubmitAsync()
        {
            if (string.IsNullOrWhiteSpace(_newName) ||
                string.IsNullOrWhiteSpace(_newProjectPath))
            {
                _addError = "Name and project path are required.";
                Repaint();
                return;
            }

            _adding = true;
            _addError = null;
            Repaint();

            var entry = new ProjectEntry
            {
                name = _newName.Trim(),
                projectPath = _newProjectPath.Trim(),
                executeMethod = (_newExecuteMethod ?? string.Empty).Trim()
            };

            try
            {
                if (string.IsNullOrEmpty(_editingOriginalName))
                {
                    await _client.AddProjectAsync(entry, _cts.Token);
                }
                else
                {
                    await _client.UpdateProjectAsync(_editingOriginalName, entry, _cts.Token);
                }

                ClearEdit();
                ProjectsChanged?.Invoke();
                await ReloadAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _addError = ex.Message;
            }
            finally
            {
                _adding = false;
                Repaint();
            }
        }

        private async UniTaskVoid DeleteAsync(string name)
        {
            if (!EditorUtility.DisplayDialog(
                    "Remove project",
                    $"Remove '{name}' from the build list? The Unity project itself is not touched.",
                    "Remove",
                    "Cancel"))
            {
                return;
            }

            try
            {
                await _client.DeleteProjectAsync(name, _cts.Token);
                ProjectsChanged?.Invoke();
                await ReloadAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _loadError = ex.Message;
                Repaint();
            }
        }
    }
}
#endif
