#if UNITY_EDITOR
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LumenvilLite.Models;
using LumenvilLite.Services;
using LumenvilLite.Settings;
using LumenvilLite.UI;
using UnityEditor;
using UnityEngine;

namespace LumenvilLite
{
    public class LumenvilLiteWindow : EditorWindow
    {
        private enum ConnectionState { Unknown, Online, Offline }

        private LumenvilLiteClient _client;
        private CancellationTokenSource _pollCts;
        private double _lastPollTime;
        private bool _showSettings;
        private bool _showLogTail;

        private ConnectionState _connectionState = ConnectionState.Unknown;
        private string _connectionMessage;
        private long _pingMs;

        private StatusResponse _lastStatus;
        private string _previousBuildStatus;
        private DateTime _lastTransitionTime;
        private int _killInFlightPid = -1;

        // Build trigger state.
        private static readonly string[] BuildTargets = { "StandaloneWindows64" };
        private static readonly string[] BuildBackends = { "Il2cpp", "Mono" };
        private ProjectEntry[] _projects = Array.Empty<ProjectEntry>();
        private int _selectedProjectIndex;
        private int _selectedTargetIndex;
        private int _selectedBackendIndex;
        private string _buildDefines = string.Empty;
        private bool _buildStartInFlight;
        private bool _buildCancelInFlight;
        private string _buildTriggerMessage;
        private double _lastProjectsRefresh;
        private const double ProjectsRefreshInterval = 30;

        private Vector2 _scrollPosition;

        private GUIStyle _titleStyle;
        private GUIStyle _sectionHeaderStyle;
        private GUIStyle _cardStyle;
        private GUIStyle _labelMutedStyle;
        private GUIStyle _logLineStyle;
        private bool _stylesInitialized;

        [MenuItem("Tools/Lumenvil Lite", priority = 200)]
        public static void Open()
        {
            var window = GetWindow<LumenvilLiteWindow>("Lumenvil Lite");
            window.minSize = new Vector2(420, 480);
            window.Show();
        }

        private void OnEnable()
        {
            _client = new LumenvilLiteClient();
            _pollCts = new CancellationTokenSource();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = null;
        }

        private void OnEditorUpdate()
        {
            var interval = Mathf.Max(1f, LumenvilLiteSettings.PollIntervalSeconds);
            if (EditorApplication.timeSinceStartup - _lastPollTime < interval)
            {
                return;
            }

            _lastPollTime = EditorApplication.timeSinceStartup;
            PollAsync(_pollCts.Token).Forget();
        }

        private async UniTaskVoid PollAsync(CancellationToken cancellationToken)
        {
            var startedAt = DateTime.UtcNow;
            try
            {
                var status = await _client.GetStatusAsync(cancellationToken);
                _pingMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                _lastStatus = status;
                _connectionState = ConnectionState.Online;
                _connectionMessage = null;
                DetectBuildTransition(status);
                Repaint();
            }
            catch (OperationCanceledException)
            {
                // Window closing or settings changed — silently ignore.
            }
            catch (LumenvilLiteRequestException ex)
            {
                _connectionState = ConnectionState.Offline;
                _connectionMessage = ShortenError(ex.Message);
                Repaint();
            }
            catch (Exception ex)
            {
                _connectionState = ConnectionState.Offline;
                _connectionMessage = ShortenError(ex.Message);
                Repaint();
            }
        }

        private void DetectBuildTransition(StatusResponse status)
        {
            var current = status?.build?.status;
            if (string.IsNullOrEmpty(current))
            {
                return;
            }

            if (_previousBuildStatus == null)
            {
                _previousBuildStatus = current;
                return;
            }

            if (_previousBuildStatus == "Building" && current != "Building")
            {
                _lastTransitionTime = DateTime.Now;
                NotifyBuildFinished(current);
                if (current == "Failed")
                {
                    _showLogTail = true; // surface error context automatically
                }
            }

            _previousBuildStatus = current;
        }

        private void NotifyBuildFinished(string finalStatus)
        {
            var label = finalStatus switch
            {
                "Success"   => "Build Succeeded",
                "Failed"    => "Build Failed",
                "Cancelled" => "Build Cancelled",
                _           => $"Build {finalStatus}"
            };
            ShowNotification(new GUIContent(label));
            EditorApplication.Beep();
        }

        private static string ShortenError(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;
            return message.Length > 200 ? message.Substring(0, 200) + "…" : message;
        }

        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.BeginVertical();

            DrawTitleBar();
            DrawConnectionPanel();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawUnityPanel();
            DrawBuildTriggerPanel();
            DrawBuildPanel();
            EditorGUILayout.EndScrollView();

            if (_showSettings)
            {
                DrawSettingsPanel();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTitleBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Lumenvil Lite", _titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_showSettings ? "Hide Settings" : "Settings", GUILayout.Width(110)))
            {
                _showSettings = !_showSettings;
            }
            EditorGUILayout.EndHorizontal();
            DrawSeparator();
        }

        private void DrawConnectionPanel()
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            EditorGUILayout.BeginHorizontal();
            DrawDot(GetConnectionColor());
            var text = _connectionState switch
            {
                ConnectionState.Online  => $"Online — {LumenvilLiteSettings.Host}:{LumenvilLiteSettings.Port}    ping {_pingMs}ms",
                ConnectionState.Offline => $"Offline — {LumenvilLiteSettings.Host}:{LumenvilLiteSettings.Port}",
                _                       => $"Connecting — {LumenvilLiteSettings.Host}:{LumenvilLiteSettings.Port}"
            };
            GUILayout.Label(text);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            {
                _lastPollTime = 0;
            }
            EditorGUILayout.EndHorizontal();

            if (_connectionState == ConnectionState.Offline && !string.IsNullOrEmpty(_connectionMessage))
            {
                EditorGUILayout.LabelField("Makineye ulaşılamıyor.", _labelMutedStyle);
                EditorGUILayout.LabelField(_connectionMessage, _labelMutedStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawUnityPanel()
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("Unity", _sectionHeaderStyle);

            var unity = _lastStatus?.unity;
            if (unity == null)
            {
                EditorGUILayout.LabelField("—", _labelMutedStyle);
                EditorGUILayout.EndVertical();
                return;
            }

            if (!unity.running || unity.count == 0)
            {
                EditorGUILayout.BeginHorizontal();
                DrawDot(new Color(0.55f, 0.55f, 0.55f));
                GUILayout.Label("No Unity processes running");
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawDot(new Color(0.92f, 0.74f, 0.18f));
            GUILayout.Label($"{unity.count} process{(unity.count > 1 ? "es" : string.Empty)} running");
            EditorGUILayout.EndHorizontal();

            if (unity.processes != null)
            {
                foreach (var process in unity.processes)
                {
                    var typeLabel = process.type switch
                    {
                        "Editor"     => "Editor",
                        "BatchBuild" => "Batch Build",
                        _            => "Unity"
                    };
                    var ramGb = process.ramBytes / 1024.0 / 1024.0 / 1024.0;
                    var uptime = TimeSpan.FromSeconds(process.uptimeSeconds);
                    var project = string.IsNullOrEmpty(process.projectPath)
                        ? "(unknown project)"
                        : System.IO.Path.GetFileName(process.projectPath.TrimEnd('/', '\\'));

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(
                        $"  • {typeLabel} — {project}    RAM {ramGb:0.0}GB    {FormatUptime(uptime)}");
                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(_killInFlightPid == process.pid))
                    {
                        if (GUILayout.Button("Quit", GUILayout.Width(54)))
                        {
                            ConfirmAndKillAsync(process, typeLabel, project, force: false).Forget();
                        }
                        if (GUILayout.Button("Force", GUILayout.Width(54)))
                        {
                            ConfirmAndKillAsync(process, typeLabel, project, force: true).Forget();
                        }
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private async UniTaskVoid ConfirmAndKillAsync(
            UnityProcessInfo process, string typeLabel, string project, bool force)
        {
            var title = force ? "Force kill Unity process?" : "Quit Unity process?";
            var body = force
                ? $"Force-kill {typeLabel} on '{project}' (pid {process.pid})?\n\n" +
                  "Force skips the save prompt and terminates immediately. " +
                  "Any unsaved scenes/assets will be lost."
                : $"Quit {typeLabel} on '{project}' (pid {process.pid})?\n\n" +
                  "Unity will get its normal close-window signal. If a save " +
                  "prompt appears on the build machine the request will time " +
                  "out after 5s and the server will fall back to a hard kill.";
            var confirm = force ? "Force Kill" : "Quit";

            if (!EditorUtility.DisplayDialog(title, body, confirm, "Cancel"))
            {
                return;
            }

            _killInFlightPid = process.pid;
            Repaint();
            try
            {
                var response = await _client.KillUnityProcessAsync(process.pid, force, _pollCts.Token);
                var ok = response != null && response.killed;
                var label = ok
                    ? $"Killed pid {process.pid} ({response.method})"
                    : $"Kill failed: {response?.error ?? "unknown error"}";
                ShowNotification(new GUIContent(label));
                _lastPollTime = 0; // Refresh the panel immediately.
            }
            catch (OperationCanceledException)
            {
                // Window closed mid-flight, swallow.
            }
            catch (LumenvilLiteRequestException ex)
            {
                ShowNotification(new GUIContent($"Kill failed: {ShortenError(ex.Message)}"));
            }
            finally
            {
                _killInFlightPid = -1;
                Repaint();
            }
        }

        private void DrawBuildTriggerPanel()
        {
            EditorGUILayout.BeginVertical(_cardStyle);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Build Trigger", _sectionHeaderStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Manage Projects...", GUILayout.Width(150)))
            {
                ProjectManagerWindow.Open(() => RefreshProjectsAsync(force: true).Forget());
            }
            EditorGUILayout.EndHorizontal();

            MaybeRefreshProjects();

            if (_projects.Length == 0)
            {
                EditorGUILayout.LabelField(
                    "No projects registered yet. Use 'Manage Projects...' to add one.",
                    _labelMutedStyle);
                EditorGUILayout.EndVertical();
                return;
            }

            // Project dropdown.
            var projectNames = new string[_projects.Length];
            for (int i = 0; i < _projects.Length; i++)
            {
                projectNames[i] = _projects[i].name ?? "(unnamed)";
            }
            _selectedProjectIndex = Mathf.Clamp(_selectedProjectIndex, 0, _projects.Length - 1);
            _selectedProjectIndex = EditorGUILayout.Popup("Project", _selectedProjectIndex, projectNames);

            // Target dropdown.
            _selectedTargetIndex = EditorGUILayout.Popup("Target", _selectedTargetIndex, BuildTargets);

            // Backend dropdown.
            _selectedBackendIndex = EditorGUILayout.Popup("Backend", _selectedBackendIndex, BuildBackends);

            // Defines.
            _buildDefines = EditorGUILayout.TextField(
                new GUIContent("Defines", "Optional, semicolon-separated. Leave empty to skip."),
                _buildDefines);

            if (!string.IsNullOrEmpty(_buildTriggerMessage))
            {
                EditorGUILayout.HelpBox(_buildTriggerMessage, MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_buildStartInFlight || _buildCancelInFlight))
            {
                if (GUILayout.Button("Start Build", GUILayout.Height(28)))
                {
                    StartBuildAsync().Forget();
                }
            }
            using (new EditorGUI.DisabledScope(_buildStartInFlight || _buildCancelInFlight))
            {
                if (GUILayout.Button("Cancel Build", GUILayout.Height(28)))
                {
                    CancelBuildAsync().Forget();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void MaybeRefreshProjects()
        {
            if (EditorApplication.timeSinceStartup - _lastProjectsRefresh > ProjectsRefreshInterval)
            {
                RefreshProjectsAsync(force: false).Forget();
            }
        }

        private async UniTaskVoid RefreshProjectsAsync(bool force)
        {
            _lastProjectsRefresh = EditorApplication.timeSinceStartup;
            try
            {
                var response = await _client.GetProjectsAsync(_pollCts.Token);
                _projects = response?.projects ?? Array.Empty<ProjectEntry>();
                Repaint();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (force)
                {
                    _buildTriggerMessage = $"Failed to load projects: {ShortenError(ex.Message)}";
                    Repaint();
                }
            }
        }

        private async UniTaskVoid StartBuildAsync()
        {
            if (_projects.Length == 0)
            {
                return;
            }
            var project = _projects[_selectedProjectIndex];
            var target  = BuildTargets[_selectedTargetIndex];
            var backend = BuildBackends[_selectedBackendIndex];

            // Local pre-check: if our last status snapshot already shows the
            // editor open on this project, prompt the user before we even hit
            // the server.
            if (IsEditorOpenOnProject(project.projectPath, out var openPid))
            {
                EditorUtility.DisplayDialog(
                    "Editor open",
                    $"A Unity Editor instance (pid {openPid}) is already open on '{project.projectPath}'.\n\n" +
                    "Quit it from the Unity panel before starting a build.",
                    "OK");
                return;
            }

            var defines = string.IsNullOrWhiteSpace(_buildDefines) ? null : _buildDefines.Trim();
            var methodLabel = string.IsNullOrWhiteSpace(project.executeMethod)
                ? "Method:  (built-in LumenvilLiteBuilder.Build)"
                : $"Method:  {project.executeMethod}";
            var prompt =
                $"Start build for '{project.name}'?\n\n" +
                $"Target:  {target}\n" +
                $"Backend: {backend}\n" +
                (string.IsNullOrEmpty(defines) ? string.Empty : $"Defines: {defines}\n") +
                methodLabel;

            if (!EditorUtility.DisplayDialog("Start build", prompt, "Start", "Cancel"))
            {
                return;
            }

            _buildStartInFlight = true;
            _buildTriggerMessage = null;
            Repaint();
            try
            {
                var response = await _client.StartBuildAsync(new BuildStartRequest
                {
                    projectName = project.name,
                    target = target,
                    backend = backend,
                    defines = defines
                }, _pollCts.Token);

                if (response != null && response.started)
                {
                    var info = response.build;
                    var output = info != null ? info.outputPath : "(unknown path)";
                    ShowNotification(new GUIContent($"Build started: {project.name}"));
                    _buildTriggerMessage = $"Build pid {info?.pid}, output: {output}";
                    _lastPollTime = 0;
                }
                else
                {
                    var err = response?.error ?? "Unknown error.";
                    _buildTriggerMessage = err;
                    EditorUtility.DisplayDialog("Build refused", err, "OK");
                }
            }
            catch (OperationCanceledException) { }
            catch (LumenvilLiteRequestException ex)
            {
                _buildTriggerMessage = ShortenError(ex.Message);
                EditorUtility.DisplayDialog("Build request failed", ex.Message, "OK");
            }
            finally
            {
                _buildStartInFlight = false;
                Repaint();
            }
        }

        private async UniTaskVoid CancelBuildAsync()
        {
            if (!EditorUtility.DisplayDialog(
                    "Cancel build",
                    "Kill the active build process? The partial output folder will remain on disk.",
                    "Cancel Build",
                    "Keep Running"))
            {
                return;
            }

            _buildCancelInFlight = true;
            Repaint();
            try
            {
                var response = await _client.CancelBuildAsync(_pollCts.Token);
                if (response != null && response.cancelled)
                {
                    ShowNotification(new GUIContent("Build cancelled"));
                    _buildTriggerMessage = null;
                    _lastPollTime = 0;
                }
                else
                {
                    var err = response?.error ?? "Cancel failed.";
                    _buildTriggerMessage = err;
                    EditorUtility.DisplayDialog("Cancel failed", err, "OK");
                }
            }
            catch (OperationCanceledException) { }
            catch (LumenvilLiteRequestException ex)
            {
                _buildTriggerMessage = ShortenError(ex.Message);
                EditorUtility.DisplayDialog("Cancel request failed", ex.Message, "OK");
            }
            finally
            {
                _buildCancelInFlight = false;
                Repaint();
            }
        }

        private void DrawBuildResultBanner(string status)
        {
            string label;
            Color background;
            switch (status)
            {
                case "Success":
                    label = "✓  Build succeeded";
                    background = new Color(0.20f, 0.55f, 0.30f, 0.85f);
                    break;
                case "Failed":
                    label = "✕  Build failed";
                    background = new Color(0.70f, 0.25f, 0.25f, 0.90f);
                    break;
                case "Cancelled":
                    label = "⊘  Build cancelled";
                    background = new Color(0.70f, 0.55f, 0.20f, 0.85f);
                    break;
                default:
                    return;
            }

            var rect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, background);
            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
                fontSize = 13
            };
            GUI.Label(rect, label, labelStyle);
            EditorGUILayout.Space(4);
        }

        private bool IsEditorOpenOnProject(string projectPath, out int pid)
        {
            pid = 0;
            if (string.IsNullOrEmpty(projectPath) || _lastStatus?.unity?.processes == null)
            {
                return false;
            }
            var normalized = projectPath.Replace('/', '\\').TrimEnd('\\');
            foreach (var p in _lastStatus.unity.processes)
            {
                if (p.type != "Editor" || string.IsNullOrEmpty(p.projectPath)) continue;
                var candidate = p.projectPath.Replace('/', '\\').TrimEnd('\\');
                if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    pid = p.pid;
                    return true;
                }
            }
            return false;
        }

        private void DrawBuildPanel()
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("Build", _sectionHeaderStyle);

            var build = _lastStatus?.build;
            if (build == null)
            {
                EditorGUILayout.LabelField("—", _labelMutedStyle);
                EditorGUILayout.EndVertical();
                return;
            }

            // Big result banner so success / failure is unmissable.
            DrawBuildResultBanner(build.status);

            EditorGUILayout.BeginHorizontal();
            DrawDot(GetBuildColor(build.status));
            var statusText = string.IsNullOrEmpty(build.currentPhase)
                ? build.status
                : $"{build.status} — {build.currentPhase}";
            GUILayout.Label(statusText);
            EditorGUILayout.EndHorizontal();

            // Active build context — what we asked the server to build, and
            // where the artefact will land. Shown so the user can find the
            // output even after a successful build banner clears.
            var active = _lastStatus?.activeBuild;
            var last = _lastStatus?.lastBuild;
            if (active != null && !string.IsNullOrEmpty(active.outputPath))
            {
                EditorGUILayout.LabelField(
                    $"{active.projectName} • {active.target} • {active.backend}",
                    _labelMutedStyle);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Output: {active.outputPath}", _labelMutedStyle);
                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                {
                    EditorGUIUtility.systemCopyBuffer = active.outputPath;
                    ShowNotification(new GUIContent("Output path copied"));
                }
                EditorGUILayout.EndHorizontal();
            }
            else if (last != null && !string.IsNullOrEmpty(last.outputPath))
            {
                EditorGUILayout.LabelField(
                    $"{last.projectName} • {last.target} • {last.backend}",
                    _labelMutedStyle);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Output: {last.outputPath}", _labelMutedStyle);
                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                {
                    EditorGUIUtility.systemCopyBuffer = last.outputPath;
                    ShowNotification(new GUIContent("Output path copied"));
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(
                    $"Exit code: {last.exitCode}    Started: {last.startedAtUtc}    Finished: {last.finishedAtUtc}",
                    _labelMutedStyle);
            }

            if (!string.IsNullOrEmpty(build.lastLogLine))
            {
                EditorGUILayout.LabelField($"Last: {Trim(build.lastLogLine, 120)}", _labelMutedStyle);
            }

            if (!string.IsNullOrEmpty(build.errorSummary))
            {
                EditorGUILayout.LabelField($"Error: {Trim(build.errorSummary, 200)}", _labelMutedStyle);
            }

            if (!string.IsNullOrEmpty(build.finishedAtUtc))
            {
                EditorGUILayout.LabelField($"Finished: {build.finishedAtUtc}", _labelMutedStyle);
            }

            if (build.logTail != null && build.logTail.Length > 0)
            {
                _showLogTail = EditorGUILayout.Foldout(_showLogTail, $"Log tail ({build.logTail.Length} lines)");
                if (_showLogTail)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    foreach (var line in build.logTail)
                    {
                        GUILayout.Label(line, _logLineStyle);
                    }
                    EditorGUILayout.EndVertical();
                }
            }

            if (!string.IsNullOrEmpty(build.logFilePath))
            {
                EditorGUILayout.LabelField($"Log: {build.logFilePath}", _labelMutedStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSettingsPanel()
        {
            DrawSeparator();
            EditorGUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("Settings", _sectionHeaderStyle);

            var newHost = EditorGUILayout.TextField("Host", LumenvilLiteSettings.Host);
            if (newHost != LumenvilLiteSettings.Host)
            {
                LumenvilLiteSettings.Host = newHost;
                ResetConnectionState();
            }

            var newPort = EditorGUILayout.IntField("Port", LumenvilLiteSettings.Port);
            if (newPort != LumenvilLiteSettings.Port)
            {
                LumenvilLiteSettings.Port = Mathf.Clamp(newPort, 1, 65535);
                ResetConnectionState();
            }

            var newPoll = EditorGUILayout.Slider("Poll interval (s)", LumenvilLiteSettings.PollIntervalSeconds, 1f, 30f);
            if (!Mathf.Approximately(newPoll, LumenvilLiteSettings.PollIntervalSeconds))
            {
                LumenvilLiteSettings.PollIntervalSeconds = newPoll;
            }

            var newTimeout = EditorGUILayout.Slider("Timeout (s)", LumenvilLiteSettings.TimeoutSeconds, 1f, 30f);
            if (!Mathf.Approximately(newTimeout, LumenvilLiteSettings.TimeoutSeconds))
            {
                LumenvilLiteSettings.TimeoutSeconds = newTimeout;
            }

            EditorGUILayout.EndVertical();
        }

        private void ResetConnectionState()
        {
            _connectionState = ConnectionState.Unknown;
            _lastStatus = null;
            _lastPollTime = 0;
        }

        private Color GetConnectionColor()
        {
            return _connectionState switch
            {
                ConnectionState.Online  => new Color(0.30f, 0.78f, 0.40f),
                ConnectionState.Offline => new Color(0.85f, 0.30f, 0.30f),
                _                       => new Color(0.55f, 0.55f, 0.55f)
            };
        }

        private static Color GetBuildColor(string status)
        {
            return status switch
            {
                "Building"  => new Color(0.32f, 0.62f, 0.95f),
                "Success"   => new Color(0.30f, 0.78f, 0.40f),
                "Failed"    => new Color(0.85f, 0.30f, 0.30f),
                "Cancelled" => new Color(0.85f, 0.65f, 0.20f),
                "Idle"      => new Color(0.55f, 0.55f, 0.55f),
                _           => new Color(0.55f, 0.55f, 0.55f)
            };
        }

        private void DrawDot(Color color)
        {
            var rect = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
            rect.y += 3;
            EditorGUI.DrawRect(rect, color);
        }

        private void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));
        }

        private static string FormatUptime(TimeSpan span)
        {
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h{span.Minutes:00}m";
            if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m{span.Seconds:00}s";
            return $"{(int)span.TotalSeconds}s";
        }

        private static string Trim(string input, int max)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Length <= max ? input : input.Substring(0, max) + "…";
        }

        private void EnsureStyles()
        {
            if (_stylesInitialized) return;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                padding = new RectOffset(4, 4, 4, 4)
            };
            _sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                padding = new RectOffset(0, 0, 2, 4)
            };
            _cardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(4, 4, 4, 4)
            };
            _labelMutedStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.65f, 0.65f, 0.65f) },
                wordWrap = true
            };
            _logLineStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                wordWrap = false,
                richText = false
            };
            _stylesInitialized = true;
        }
    }
}
#endif
