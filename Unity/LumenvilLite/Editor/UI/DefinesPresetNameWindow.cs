#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LumenvilLite.UI
{
    public class DefinesPresetNameWindow : EditorWindow
    {
        private const string k_NameControl = "DefinesPresetName";
        private const float k_ButtonWidth = 80f;
        private const float k_CenterRatio = 0.5f;

        private static readonly Vector2 s_Size = new Vector2(340f, 92f);
        private static readonly GUIContent s_NameLabel = new GUIContent("Name");

        private string m_Name = string.Empty;
        private PresetNameValidator m_Validate;
        private PresetNameConfirmedHandler m_Confirmed;
        private bool m_IsFocusPending = true;

        private void OnGUI()
        {
            string trimmedName = m_Name.Trim();
            string error = m_Validate(trimmedName);
            HandleKeyboard(trimmedName, error);

            GUI.SetNextControlName(k_NameControl);
            m_Name = EditorGUILayout.TextField(s_NameLabel, m_Name);
            FocusNameFieldOnce();

            EditorGUILayout.LabelField(error ?? string.Empty, EditorStyles.miniLabel);
            DrawButtons(trimmedName, error);
        }

        public static void Open(string title, string initialName, PresetNameValidator validate,
            PresetNameConfirmedHandler confirmed)
        {
            var window = CreateInstance<DefinesPresetNameWindow>();
            window.titleContent = new GUIContent(title);
            window.m_Name = initialName;
            window.m_Validate = validate;
            window.m_Confirmed = confirmed;
            window.minSize = s_Size;
            window.maxSize = s_Size;
            window.position = CenterOnMainWindow(s_Size);
            window.ShowModalUtility();
        }

        private static Rect CenterOnMainWindow(Vector2 size)
        {
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            return new Rect(main.center - size * k_CenterRatio, size);
        }

        private void DrawButtons(string trimmedName, string error)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(k_ButtonWidth)))
                Close();

            using (new EditorGUI.DisabledScope(error != null))
            {
                if (GUILayout.Button("Save", GUILayout.Width(k_ButtonWidth)))
                    Confirm(trimmedName);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void HandleKeyboard(string trimmedName, string error)
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown)
                return;

            if (current.keyCode == KeyCode.Escape)
            {
                current.Use();
                Close();
            }
            else if ((current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter) && error == null)
            {
                current.Use();
                Confirm(trimmedName);
            }
        }

        private void FocusNameFieldOnce()
        {
            if (!m_IsFocusPending)
                return;

            EditorGUI.FocusTextInControl(k_NameControl);
            m_IsFocusPending = false;
        }

        private void Confirm(string presetName)
        {
            m_Confirmed?.Invoke(presetName);
            Close();
        }

        public delegate string PresetNameValidator(string presetName);

        public delegate void PresetNameConfirmedHandler(string presetName);
    }
}
#endif
