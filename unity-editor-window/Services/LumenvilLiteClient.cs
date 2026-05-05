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
