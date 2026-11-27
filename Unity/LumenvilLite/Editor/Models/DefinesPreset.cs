#if UNITY_EDITOR
using System;
using UnityEngine;

namespace LumenvilLite.Models
{
    [Serializable]
    public class DefinesPreset
    {
        [SerializeField] private string m_Name;
        [SerializeField] private string m_Defines;

        public DefinesPreset()
        {
        }

        public DefinesPreset(string name, string defines)
        {
            m_Name = name;
            m_Defines = defines;
        }

        public string Name
        {
            get => m_Name;
            set => m_Name = value;
        }

        public string Defines
        {
            get => m_Defines;
            set => m_Defines = value;
        }
    }
}
#endif
