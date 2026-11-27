#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using LumenvilLite.Models;
using LumenvilLite.Settings;

namespace LumenvilLite.UI
{
    public class DefinesPresetField
    {
        private const string k_CustomLabel = "Custom";
        private const string k_ModifiedSuffix = " (modified)";
        private const string k_MenuIcon = "_Menu";
        private const int k_NoPreset = -1;
        private const float k_SaveButtonWidth = 52f;
        private const float k_MenuButtonWidth = 24f;

        private static readonly GUIContent s_PresetLabel =
            new GUIContent("Preset", "Pick a saved define list, or Custom to type one by hand.");
        private static readonly GUIContent s_DefinesLabel =
            new GUIContent("Defines", "Optional, semicolon-separated. Leave empty to skip.");
        private static readonly GUIContent s_SaveContent =
            new GUIContent("Save", "Write the edited defines back into the selected preset.");
        private static readonly GUIContent s_SaveAsNewItem = new GUIContent("Save as New…");
        private static readonly GUIContent s_RenameItem = new GUIContent("Rename…");
        private static readonly GUIContent s_RevertItem = new GUIContent("Revert");
        private static readonly GUIContent s_DeleteItem = new GUIContent("Delete");

        private readonly EditorWindow m_Owner;
        private readonly GUIContent m_MenuContent;
        private readonly List<DefinesPreset> m_Presets = new List<DefinesPreset>();
        private string[] m_OptionLabels = { k_CustomLabel };
        private int m_SelectedIndex = k_NoPreset;
        private string m_PendingDefines;

        public DefinesPresetField(EditorWindow owner)
        {
            m_Owner = owner;
            m_MenuContent = new GUIContent(EditorGUIUtility.IconContent(k_MenuIcon)) { tooltip = "Preset actions" };
        }

        public string SelectedDefines => HasSelection ? SelectedPreset.Defines : null;

        private bool HasSelection => m_SelectedIndex != k_NoPreset;
        private DefinesPreset SelectedPreset => m_Presets[m_SelectedIndex];

        public void Reload()
        {
            DefinesPresetStore.Load(m_Presets);
            RebuildOptionLabels();
            Select(FindIndex(LumenvilLiteSettings.SelectedDefinesPreset));
        }

        public string Draw(string defines)
        {
            defines = ConsumePendingDefines(defines);

            EditorGUILayout.BeginHorizontal();
            defines = DrawPresetPopup(defines);
            DrawSaveButton(defines);
            DrawMenuButton(defines);
            EditorGUILayout.EndHorizontal();

            return EditorGUILayout.TextField(s_DefinesLabel, defines);
        }

        public string DescribeSelection(string defines)
        {
            if (!HasSelection)
                return k_CustomLabel;

            return IsModified(defines) ? SelectedPreset.Name + k_ModifiedSuffix : SelectedPreset.Name;
        }

        private static void AddItem(GenericMenu menu, GUIContent content, bool isEnabled,
            GenericMenu.MenuFunction action)
        {
            if (isEnabled)
                menu.AddItem(content, false, action);
            else
                menu.AddDisabledItem(content);
        }

        private string ConsumePendingDefines(string defines)
        {
            if (m_PendingDefines == null)
                return defines;

            string pending = m_PendingDefines;
            m_PendingDefines = null;
            return pending;
        }

        private string DrawPresetPopup(string defines)
        {
            int selectedIndex = EditorGUILayout.Popup(s_PresetLabel, m_SelectedIndex + 1, m_OptionLabels) - 1;
            if (selectedIndex == m_SelectedIndex)
                return defines;

            Select(selectedIndex);
            return HasSelection ? SelectedPreset.Defines : defines;
        }

        private void DrawSaveButton(string defines)
        {
            using (new EditorGUI.DisabledScope(!IsModified(defines)))
            {
                if (GUILayout.Button(s_SaveContent, GUILayout.Width(k_SaveButtonWidth)))
                    UpdateSelectedPreset(defines);
            }
        }

        private void DrawMenuButton(string defines)
        {
            Rect buttonRect = GUILayoutUtility.GetRect(m_MenuContent, EditorStyles.miniButton,
                GUILayout.Width(k_MenuButtonWidth));
            if (GUI.Button(buttonRect, m_MenuContent, EditorStyles.miniButton))
                BuildMenu(defines).DropDown(buttonRect);
        }

        private GenericMenu BuildMenu(string defines)
        {
            var menu = new GenericMenu();
            menu.AddItem(s_SaveAsNewItem, false, () => PromptCreate(defines));
            AddItem(menu, s_RenameItem, HasSelection, PromptRename);
            AddItem(menu, s_RevertItem, IsModified(defines), Revert);
            menu.AddSeparator(string.Empty);
            AddItem(menu, s_DeleteItem, HasSelection, DeleteSelectedPreset);
            return menu;
        }

        private void PromptCreate(string defines)
        {
            DefinesPresetNameWindow.Open("New Defines Preset", string.Empty,
                presetName => ValidateName(presetName, k_NoPreset),
                presetName => CreatePreset(presetName, defines));
        }

        private void PromptRename()
        {
            int renamedIndex = m_SelectedIndex;
            DefinesPresetNameWindow.Open("Rename Defines Preset", SelectedPreset.Name,
                presetName => ValidateName(presetName, renamedIndex),
                presetName => RenamePreset(renamedIndex, presetName));
        }

        private string ValidateName(string presetName, int ignoredIndex)
        {
            if (presetName.Length == 0)
                return "Enter a name.";

            int existingIndex = FindIndex(presetName);
            return existingIndex == k_NoPreset || existingIndex == ignoredIndex
                ? null
                : $"A preset named '{presetName}' already exists.";
        }

        private bool IsModified(string defines)
        {
            return HasSelection && !string.Equals(SelectedPreset.Defines, defines, StringComparison.Ordinal);
        }

        private void UpdateSelectedPreset(string defines)
        {
            SelectedPreset.Defines = defines;
            DefinesPresetStore.Save(m_Presets);
        }

        private void CreatePreset(string presetName, string defines)
        {
            m_Presets.Add(new DefinesPreset(presetName, defines));
            SaveAndSelect(m_Presets.Count - 1);
        }

        private void RenamePreset(int index, string presetName)
        {
            m_Presets[index].Name = presetName;
            SaveAndSelect(index);
        }

        private void Revert()
        {
            m_PendingDefines = SelectedPreset.Defines;
            m_Owner.Repaint();
        }

        private void DeleteSelectedPreset()
        {
            string presetName = SelectedPreset.Name;
            if (!EditorUtility.DisplayDialog("Delete preset", $"Delete the '{presetName}' defines preset?",
                    "Delete", "Cancel"))
                return;

            m_Presets.RemoveAt(m_SelectedIndex);
            SaveAndSelect(k_NoPreset);
        }

        private void SaveAndSelect(int index)
        {
            DefinesPresetStore.Save(m_Presets);
            RebuildOptionLabels();
            Select(index);
            m_Owner.Repaint();
        }

        private void Select(int index)
        {
            m_SelectedIndex = index;
            LumenvilLiteSettings.SelectedDefinesPreset = HasSelection ? SelectedPreset.Name : string.Empty;
        }

        private int FindIndex(string presetName)
        {
            for (int i = 0; i < m_Presets.Count; i++)
            {
                if (string.Equals(m_Presets[i].Name, presetName, StringComparison.Ordinal))
                    return i;
            }

            return k_NoPreset;
        }

        private void RebuildOptionLabels()
        {
            m_OptionLabels = new string[m_Presets.Count + 1];
            m_OptionLabels[0] = k_CustomLabel;
            for (int i = 0; i < m_Presets.Count; i++)
                m_OptionLabels[i + 1] = m_Presets[i].Name;
        }
    }
}
#endif
