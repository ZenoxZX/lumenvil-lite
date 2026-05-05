#if UNITY_EDITOR
using UnityEditor;

namespace LumenvilLite.Settings
{
    public static class LumenvilLiteSettings
    {
        private const string HostKey = "LumenvilLite.Host";
        private const string PortKey = "LumenvilLite.Port";
        private const string PollIntervalKey = "LumenvilLite.PollIntervalSeconds";
        private const string TimeoutKey = "LumenvilLite.TimeoutSeconds";

        public const string DefaultHost = "localhost";
        public const int DefaultPort = 5151;
        public const float DefaultPollInterval = 3f;
        public const float DefaultTimeout = 5f;

        public static string Host
        {
            get => EditorPrefs.GetString(HostKey, DefaultHost);
            set => EditorPrefs.SetString(HostKey, value);
        }

        public static int Port
        {
            get => EditorPrefs.GetInt(PortKey, DefaultPort);
            set => EditorPrefs.SetInt(PortKey, value);
        }

        public static float PollIntervalSeconds
        {
            get => EditorPrefs.GetFloat(PollIntervalKey, DefaultPollInterval);
            set => EditorPrefs.SetFloat(PollIntervalKey, value);
        }

        public static float TimeoutSeconds
        {
            get => EditorPrefs.GetFloat(TimeoutKey, DefaultTimeout);
            set => EditorPrefs.SetFloat(TimeoutKey, value);
        }

        public static string BaseUrl => $"http://{Host}:{Port}";
    }
}
#endif
