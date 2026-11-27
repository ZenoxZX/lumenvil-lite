#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using LumenvilLite.Models;

namespace LumenvilLite.Settings
{
    public static class DefinesPresetStore
    {
        private const string k_RelativePath =
            "ProjectSettings/Packages/com.zenoxzx.lumenvil-lite/DefinesPresets.json";

        public static string FilePath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, k_RelativePath);

        public static void Load(List<DefinesPreset> presets)
        {
            presets.Clear();
            string path = FilePath;
            if (!File.Exists(path))
                return;

            try
            {
                var collection = JsonUtility.FromJson<DefinesPresetCollection>(File.ReadAllText(path));
                if (collection?.Presets != null)
                    presets.AddRange(collection.Presets);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError($"[Lumenvil Lite] Could not parse defines presets at '{path}': {exception.Message}");
            }
        }

        public static void Save(List<DefinesPreset> presets)
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var collection = new DefinesPresetCollection { Presets = presets };
            File.WriteAllText(path, JsonUtility.ToJson(collection, true));
        }

        [Serializable]
        private class DefinesPresetCollection
        {
            [SerializeField] private List<DefinesPreset> m_Presets = new List<DefinesPreset>();

            public List<DefinesPreset> Presets
            {
                get => m_Presets;
                set => m_Presets = value;
            }
        }
    }
}
#endif
