using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UniVRM10;

public class DeveloppingToolsController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI logText = null;
    [SerializeField] private ApiResources apiResources = null;
    private string logApiUrl = "";
    public bool IsSaving { get; private set; }

    void Start()
    {
        logApiUrl = apiResources.logApiUrl;
        if (logText == null)
        {
            Debug.LogError("[DeveloppingToolsController] Log Text is null");
        }
        else
        {
            logText.text = "Turn,UserSpeech,AiReply,TotalTime_ms,TTSTime_ms,TTMTime_ms\n";
        }
    }

    public void AddLog(string log)
    {
        if (string.IsNullOrEmpty(log)) return;

        if (!log.EndsWith("\n"))
        {
            log += "\n";
        }

        logText.text += log;
    }
    public void ResetLog()
    {
        logText.text = string.Empty;
    }

    [Serializable]
    public class LogData { public string log_content; }

    public IEnumerator SaveLogCoroutine()
    {
        if (string.IsNullOrEmpty(logText.text))
        {
            Debug.LogWarning("Log is empty. Skipping save.");
            yield break;
        }

        IsSaving = true;
        AddLog("<color=yellow>System: Saving logs to server...</color>");

        yield return StartCoroutine(UploadLogCoroutine(logText.text));

        IsSaving = false;
        Debug.Log("[DeveloppingToolsController] Save process finished.");
    }

    private IEnumerator UploadLogCoroutine(string content)
    {
        LogData data = new LogData { log_content = content };
        string jsonData = JsonUtility.ToJson(data);

        using (UnityWebRequest request = new UnityWebRequest(logApiUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("[DeveloppingToolsController] Log uploaded successfully");
                AddLog("<color=green>[DeveloppingToolsController] Log saved to server (CSV)</color>");
            }
            else
            {
                Debug.LogError($"[DeveloppingToolsController] Upload failed: {request.error}");
                AddLog("<color=red>[DeveloppingToolsController] Failed to save log to server</color>");
            }
        }
    }
}
