#if UNITY_EDITOR
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LumenvilLite.Models;
using LumenvilLite.Settings;
using UnityEngine;
using UnityEngine.Networking;

namespace LumenvilLite.Services
{
    public sealed class LumenvilLiteClient
    {
        public async UniTask<HealthResponse> GetHealthAsync(CancellationToken cancellationToken)
        {
            return await GetJsonAsync<HealthResponse>("/health", cancellationToken);
        }

        public async UniTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken)
        {
            return await GetJsonAsync<StatusResponse>("/status", cancellationToken);
        }

        public async UniTask<KillResponse> KillUnityProcessAsync(int pid, bool force, CancellationToken cancellationToken)
        {
            var payload = JsonUtility.ToJson(new KillRequest { force = force });
            return await SendJsonAsync<KillResponse>(
                method: UnityWebRequest.kHttpVerbPOST,
                path: $"/unity/{pid}/kill",
                jsonBody: payload,
                cancellationToken: cancellationToken,
                timeoutMultiplier: 3,
                allow409Body: false);
        }

        public async UniTask<ProjectListResponse> GetProjectsAsync(CancellationToken cancellationToken)
        {
            return await GetJsonAsync<ProjectListResponse>("/projects", cancellationToken);
        }

        public async UniTask<ProjectEntry> AddProjectAsync(ProjectEntry entry, CancellationToken cancellationToken)
        {
            var payload = JsonUtility.ToJson(entry);
            return await SendJsonAsync<ProjectEntry>(
                method: UnityWebRequest.kHttpVerbPOST,
                path: "/projects",
                jsonBody: payload,
                cancellationToken: cancellationToken,
                timeoutMultiplier: 1,
                allow409Body: false);
        }

        public async UniTask DeleteProjectAsync(string name, CancellationToken cancellationToken)
        {
            await SendJsonAsync<EmptyBody>(
                method: UnityWebRequest.kHttpVerbDELETE,
                path: $"/projects/{Uri.EscapeDataString(name)}",
                jsonBody: null,
                cancellationToken: cancellationToken,
                timeoutMultiplier: 1,
                allow409Body: false,
                allowEmptyResponse: true);
        }

        public async UniTask<BuildStartResponse> StartBuildAsync(BuildStartRequest body, CancellationToken cancellationToken)
        {
            var payload = JsonUtility.ToJson(body);
            return await SendJsonAsync<BuildStartResponse>(
                method: UnityWebRequest.kHttpVerbPOST,
                path: "/build/start",
                jsonBody: payload,
                cancellationToken: cancellationToken,
                timeoutMultiplier: 2,
                allow409Body: true);
        }

        public async UniTask<ActiveBuildResponse> GetActiveBuildAsync(CancellationToken cancellationToken)
        {
            return await GetJsonAsync<ActiveBuildResponse>("/build/active", cancellationToken);
        }

        public async UniTask<BuildCancelResponse> CancelBuildAsync(CancellationToken cancellationToken)
        {
            return await SendJsonAsync<BuildCancelResponse>(
                method: UnityWebRequest.kHttpVerbPOST,
                path: "/build/cancel",
                jsonBody: null,
                cancellationToken: cancellationToken,
                timeoutMultiplier: 2,
                allow409Body: true);
        }

        [Serializable]
        private class EmptyBody { }

        private static async UniTask<T> SendJsonAsync<T>(
            string method,
            string path,
            string jsonBody,
            CancellationToken cancellationToken,
            int timeoutMultiplier,
            bool allow409Body,
            bool allowEmptyResponse = false)
            where T : class
        {
            var url = LumenvilLiteSettings.BaseUrl + path;
            using var request = new UnityWebRequest(url, method);
            if (!string.IsNullOrEmpty(jsonBody))
            {
                var bodyBytes = System.Text.Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyBytes) { contentType = "application/json" };
            }
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(5, Mathf.RoundToInt(LumenvilLiteSettings.TimeoutSeconds * timeoutMultiplier));

            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
            }
            catch (UnityWebRequestException)
            {
                // UnityWebRequest treats non-2xx as exceptions but we still want to
                // read the body for some semantic 4xx responses (409, 422). For the
                // others we re-throw with the body attached for context.
                var status = request.responseCode;
                var body = request.downloadHandler?.text ?? string.Empty;
                if (allow409Body && (status == 409 || status == 422 || status == 400))
                {
                    return ParseOrThrow<T>(body, url);
                }
                throw new LumenvilLiteRequestException(
                    $"Request {method} {url} failed: HTTP {status}{(string.IsNullOrEmpty(body) ? string.Empty : " — " + body)}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new LumenvilLiteRequestException(
                    $"Request {method} {url} failed: {request.error} (result: {request.result})");
            }

            var json = request.downloadHandler?.text ?? string.Empty;
            if (string.IsNullOrEmpty(json))
            {
                if (allowEmptyResponse)
                {
                    return null!;
                }
                throw new LumenvilLiteRequestException($"Empty response from {url}");
            }
            return ParseOrThrow<T>(json, url);
        }

        private static T ParseOrThrow<T>(string json, string url) where T : class
        {
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception ex)
            {
                throw new LumenvilLiteRequestException(
                    $"Failed to parse JSON from {url}: {ex.Message}", ex);
            }
        }

        private static async UniTask<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
            where T : class
        {
            var url = LumenvilLiteSettings.BaseUrl + path;
            using var request = UnityWebRequest.Get(url);
            request.timeout = Mathf.Max(1, Mathf.RoundToInt(LumenvilLiteSettings.TimeoutSeconds));

            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
            }
            catch (UnityWebRequestException ex)
            {
                throw new LumenvilLiteRequestException(
                    $"Request to {url} failed: {ex.Error}", ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new LumenvilLiteRequestException(
                    $"Request to {url} failed: {request.error} (result: {request.result})");
            }

            var json = request.downloadHandler?.text ?? string.Empty;
            if (string.IsNullOrEmpty(json))
            {
                throw new LumenvilLiteRequestException($"Empty response from {url}");
            }

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception ex)
            {
                throw new LumenvilLiteRequestException(
                    $"Failed to parse JSON from {url}: {ex.Message}", ex);
            }
        }
    }

    public sealed class LumenvilLiteRequestException : Exception
    {
        public LumenvilLiteRequestException(string message) : base(message) { }
        public LumenvilLiteRequestException(string message, Exception inner) : base(message, inner) { }
    }
}
#endif
