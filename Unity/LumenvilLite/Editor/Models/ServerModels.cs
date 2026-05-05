#if UNITY_EDITOR
using System;

namespace LumenvilLite.Models
{
    [Serializable]
    public class HealthResponse
    {
        public string status;
        public string name;
        public string version;
        public string hostname;
        public double uptimeSeconds;
    }

    [Serializable]
    public class UnityProcessInfo
    {
        public int pid;
        public string type;
        public string projectPath;
        public long ramBytes;
        public double uptimeSeconds;
        public string logFilePath;
    }

    [Serializable]
    public class UnityResponse
    {
        public bool running;
        public int count;
        public UnityProcessInfo[] processes;
    }

    [Serializable]
    public class BuildStatusResponse
    {
        public string status;
        public string currentPhase;
        public string lastLogLine;
        public string[] logTail;
        public string startedAtUtc;
        public string finishedAtUtc;
        public string errorSummary;
        public string logFilePath;
    }

    [Serializable]
    public class StatusResponse
    {
        public HealthResponse health;
        public UnityResponse unity;
        public BuildStatusResponse build;
    }
}
#endif
