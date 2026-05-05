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
        public ActiveBuildInfo activeBuild;
    }

    [Serializable]
    public class KillRequest
    {
        public bool force;
    }

    [Serializable]
    public class KillResponse
    {
        public int pid;
        public bool killed;
        public string method;
        public int exitCode;
        public string error;
    }

    [Serializable]
    public class ProjectEntry
    {
        public string name;
        public string projectPath;
        public string executeMethod;
    }

    [Serializable]
    public class ProjectListResponse
    {
        public ProjectEntry[] projects;
    }

    [Serializable]
    public class BuildStartRequest
    {
        public string projectName;
        public string target;
        public string backend;   // "Il2cpp" or "Mono"
        public string defines;
    }

    [Serializable]
    public class ActiveBuildInfo
    {
        public string projectName;
        public string target;
        public string backend;
        public string outputPath;
        public string logFilePath;
        public int pid;
        public string startedAtUtc;
    }

    [Serializable]
    public class BuildStartResponse
    {
        public bool started;
        public ActiveBuildInfo build;
        public string error;
        public string errorCode;
    }

    [Serializable]
    public class ActiveBuildResponse
    {
        public ActiveBuildInfo active;
    }

    [Serializable]
    public class BuildCancelResponse
    {
        public bool cancelled;
        public string error;
    }
}
#endif
