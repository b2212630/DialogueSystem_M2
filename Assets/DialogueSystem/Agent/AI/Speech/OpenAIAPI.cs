using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class OpenAIAPI : MonoBehaviour
{
    [Header("OpenAI Settings")]
    [SerializeField] private ApiResources apiResources;

    private string apiKey = null;
    private List<Message> messages = new List<Message>();

    private void Start()
    {
        LoadApiKey();
    }

    private void LoadApiKey()
    {
        if (apiResources != null)
        {
            apiKey = apiResources.openAIApiKey;
        }
        else
        {
            Debug.LogError("[OpenAIAPI] Not Found API Key Resource.");
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("[OpenAIAPI] API Key is null or empty after loading.");
        }
    }

    public void GenerativeResponse(string prompt, Action<string> onResponse)
    {
        messages.Add(new Message { role = "user", content = prompt });
        StartCoroutine(GetGptResponse(onResponse));
    }

    private IEnumerator GetGptResponse(Action<string> onResponse)
    {
        BodyChat body = new BodyChat { model = "gpt-4o", messages = messages };
        string jsonBody = JsonUtility.ToJson(body);
        string url = "https://api.openai.com/v1/chat/completions";

        using UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] postData = System.Text.Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(postData);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            ApiResponse response = JsonUtility.FromJson<ApiResponse>(request.downloadHandler.text);
            string gptText = response.choices[0].message.content;
            messages.Add(new Message { role = "assistant", content = gptText });
            onResponse?.Invoke(gptText);
        }
        else
        {
            Debug.LogError($"[OpenAIAPI] GPT Request Failed: {request.error} - {request.downloadHandler.text}");
            onResponse?.Invoke("[System Error] GPT API Error");
        }
    }

    public void TextToSpeech(string text, Action<string> onFileSaved)
    {
        StartCoroutine(GetMp3AndSave(text, onFileSaved));
    }

    private IEnumerator GetMp3AndSave(string input, Action<string> onFileSaved)
    {
        BodyTTS body = new BodyTTS { model = "tts-1", input = input, voice = "nova" };
        string jsonBody = JsonUtility.ToJson(body);
        string url = "https://api.openai.com/v1/audio/speech";

        using UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] postData = System.Text.Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(postData);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[OpenAIAPI] TTS Error: " + request.error);
            onFileSaved?.Invoke(null); // Indicate failure
        }
        else
        {
            string folderName = "SpeechAI";
            string basePath = Application.persistentDataPath;
            string fullFolderPath = Path.Combine(basePath, folderName);
            if (!Directory.Exists(fullFolderPath))
            {
                Directory.CreateDirectory(fullFolderPath);
            }

            string filePath = Path.Combine(fullFolderPath, $"audio.mp3");

            try
            {
                File.WriteAllBytes(filePath, request.downloadHandler.data);
                Debug.Log($"[OpenAIAPI] Audio file saved to: {filePath}");
                onFileSaved?.Invoke(filePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[OpenAIAPI] File write error: {e.Message}");
                onFileSaved?.Invoke(null); 
            }
        }
    }

    // Data Class
    [Serializable] public class Message { public string role; public string content; }
    [Serializable] public class BodyChat { public string model; public List<Message> messages; }
    [Serializable] public class ApiResponse { public List<Choice> choices; }
    [Serializable] public class Choice { public Response message; }
    [Serializable] public class Response { public string role; public string content; }
    [Serializable] public class BodyTTS { public string model; public string input; public string voice; }
}