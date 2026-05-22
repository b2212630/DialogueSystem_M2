using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class SpeechController : MonoBehaviour
{
    [Header("AI Speech Settings")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private OpenAIAPI openAIAPI;

    [Header("Filler Settings")]
    [SerializeField] private AudioSource fillerAudioSource;

    // Assets/Resources/Fillers を読む場合は "Fillers"
    [SerializeField] private string fillerResourcesPath = "Fillers";

    // Inspectorでは Size 0 のままでOK
    [SerializeField] private AudioClip[] fillerClips;

    // フィラー音量。AI音声より大きい場合は 0.2〜0.4 くらいに下げる
    [SerializeField, Range(0f, 1f)] private float fillerVolume = 0.35f;

    // Gesture生成中にも鳴らしたい場合はtrue
    [SerializeField] private bool playFillerDuringGestureGeneration = false;

    private string currentReply = "";
    private string currentGesture = "";
    private string speechFilePath = "";

    private Coroutine fillerCoroutine = null;

    private const string ReplyPattern = @"(?:^|\n)\s*reply:\s*(.*?)\s*(?:$|\n)";
    private const string GesturePattern = @"(?:^|\n)\s*gesture:\s*(.*?)\s*(?:$|\n)";

    private void Start()
    {
        InitializeAudioSources();
        LoadFillerClipsFromResources();

        if (openAIAPI == null)
        {
            Debug.LogError("[SpeechController] OpenAIAPI is not assigned.");
        }
    }

    private void InitializeAudioSources()
    {
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            Debug.LogWarning("[SpeechController] AudioSource was not assigned, so it was added automatically.");
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 1f;
        audioSource.mute = false;

        if (fillerAudioSource == null)
        {
            fillerAudioSource = gameObject.AddComponent<AudioSource>();
            Debug.LogWarning("[SpeechController] FillerAudioSource was not assigned, so it was added automatically.");
        }

        if (fillerAudioSource == audioSource)
        {
            fillerAudioSource = gameObject.AddComponent<AudioSource>();
            Debug.LogWarning("[SpeechController] FillerAudioSource was same as AudioSource, so a new one was added.");
        }

        fillerAudioSource.playOnAwake = false;
        fillerAudioSource.loop = false;

        // Quest実機で位置によって聞こえない問題を避ける
        fillerAudioSource.spatialBlend = 0f;
        fillerAudioSource.volume = fillerVolume;
        fillerAudioSource.mute = false;
    }

    private void LoadFillerClipsFromResources()
    {
        List<AudioClip> loadedClips = new List<AudioClip>();

        if (fillerClips != null)
        {
            foreach (AudioClip clip in fillerClips)
            {
                if (clip != null && !loadedClips.Contains(clip))
                {
                    loadedClips.Add(clip);
                }
            }
        }

        AudioClip[] resourceClips = Resources.LoadAll<AudioClip>(fillerResourcesPath);

        if (resourceClips != null)
        {
            foreach (AudioClip clip in resourceClips)
            {
                if (clip != null && !loadedClips.Contains(clip))
                {
                    loadedClips.Add(clip);
                }
            }
        }

        fillerClips = loadedClips.ToArray();

        if (fillerClips.Length == 0)
        {
            Debug.LogWarning("[SpeechController] No filler clips found. Check Assets/Resources/" + fillerResourcesPath);
            return;
        }

        Debug.Log("[SpeechController] Loaded filler clips: " + fillerClips.Length);

        foreach (AudioClip clip in fillerClips)
        {
            Debug.Log("[SpeechController] Filler loaded: " + clip.name);
        }
    }

    public IEnumerator GenerateReplyText(string prompt)
    {
        string resultText = "";

        if (openAIAPI == null)
        {
            Debug.LogError("[SpeechController] OpenAIAPI is not assigned.");
            currentReply = "a person is standing";
            yield break;
        }

        var gptResult = new AsyncResult<string>();

        // GPT返答待ちの開始時にフィラーを1回だけ再生する
        StartFiller();

        openAIAPI.GenerativeResponse(prompt, (res) =>
        {
            gptResult.Result = res;
            gptResult.IsDone = true;
        });

        yield return new WaitUntil(() => gptResult.IsDone);

        // ここでStopFillerしない
        // GPT返答が返ってきても、フィラーは最後まで再生させる

        string gptResponse = gptResult.Result;
        Debug.Log("[Reply API Raw] " + gptResponse);

        if (!string.IsNullOrEmpty(gptResponse))
        {
            string extracted = ExtractValue(gptResponse, ReplyPattern);

            if (string.IsNullOrEmpty(extracted))
            {
                if (!gptResponse.Contains("gesture:"))
                {
                    resultText = gptResponse.Trim();
                }
            }
            else
            {
                resultText = extracted;
            }
        }

        currentReply = string.IsNullOrEmpty(resultText) ? "a person is standing" : resultText;
        Debug.Log("[SpeechController] Final Reply Set: " + currentReply);
    }

    public IEnumerator GenerateSpeech()
    {
        if (string.IsNullOrEmpty(currentReply))
        {
            Debug.LogError("[SpeechController] Speech text is null or empty.");
            yield break;
        }

        if (openAIAPI == null)
        {
            Debug.LogError("[SpeechController] OpenAIAPI is not assigned.");
            yield break;
        }

        var speechResult = new AsyncResult<string>();

        // TTS生成中にフィラーが流れていてもよいので、ここではフィラーを止めない
        openAIAPI.TextToSpeech(currentReply, (path) =>
        {
            speechResult.Result = path;
            speechResult.IsDone = true;
        });

        yield return new WaitUntil(() => speechResult.IsDone);

        speechFilePath = speechResult.Result;

        if (string.IsNullOrEmpty(speechFilePath))
        {
            Debug.LogError("[SpeechController] Failed to generate speech audio.");
            yield break;
        }

        Debug.Log("[SpeechController] Speech file path set: " + speechFilePath);
    }

    public void Speak()
    {
        StartCoroutine(PlayAudio());
    }

    public IEnumerator PlayAudio()
    {
        if (string.IsNullOrEmpty(speechFilePath))
        {
            Debug.LogError("[SpeechController] SpeechFilePath is empty.");
            yield break;
        }

        // AI音声を再生する前に、フィラーが終わるまで待つ
        yield return StartCoroutine(WaitForFillerToFinish());

        if (!File.Exists(speechFilePath))
        {
            Debug.LogError("[SpeechController] Speech file does not exist: " + speechFilePath);
            yield break;
        }

        // Android/Questでも安全にfile URLへ変換
        string url = new Uri(speechFilePath).AbsoluteUri;

        using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            AudioClip audioClip = DownloadHandlerAudioClip.GetContent(request);

            audioSource.Stop();
            audioSource.clip = audioClip;
            audioSource.Play();

            Debug.Log("[SpeechController] Playing AI audio from: " + Path.GetFileName(speechFilePath));

            while (audioSource.isPlaying)
            {
                yield return null;
            }
        }
        else
        {
            Debug.LogError("[SpeechController] Audio file loading error: " + request.error);
            Debug.LogError("[SpeechController] Audio url: " + url);
        }

        DeleteSpeechFile();
    }

    public IEnumerator GenerateGestureText(string prompt)
    {
        string resultText = "";

        if (openAIAPI == null)
        {
            Debug.LogError("[SpeechController] OpenAIAPI is not assigned.");
            currentGesture = "a person is standing";
            yield break;
        }

        var gptResult = new AsyncResult<string>();

        if (playFillerDuringGestureGeneration)
        {
            StartFiller();
        }

        openAIAPI.GenerativeResponse(prompt, (res) =>
        {
            gptResult.Result = res;
            gptResult.IsDone = true;
        });

        yield return new WaitUntil(() => gptResult.IsDone);

        // ここでもStopFillerしない
        // AI音声再生前のPlayAudio()でフィラー終了を待つ

        string gptResponse = gptResult.Result;
        Debug.Log("[Gesture API Raw] " + gptResponse);

        if (!string.IsNullOrEmpty(gptResponse))
        {
            string extracted = ExtractValue(gptResponse, GesturePattern);

            if (string.IsNullOrEmpty(extracted))
            {
                if (!gptResponse.Contains("reply:"))
                {
                    resultText = gptResponse.Trim();
                }
            }
            else
            {
                resultText = extracted;
            }
        }

        currentGesture = string.IsNullOrEmpty(resultText) ? "a person is standing" : resultText;
        Debug.Log("[SpeechController] Final Gesture Set: " + currentGesture);
    }

    private void StartFiller()
    {
        if (fillerClips == null || fillerClips.Length == 0 || !HasValidFillerClip())
        {
            LoadFillerClipsFromResources();
        }

        if (fillerClips == null || fillerClips.Length == 0 || !HasValidFillerClip())
        {
            Debug.LogWarning("[SpeechController] Filler clips are not available.");
            return;
        }

        if (fillerAudioSource == null)
        {
            Debug.LogWarning("[SpeechController] FillerAudioSource is not assigned.");
            return;
        }

        // すでにフィラーが再生中なら、新しく再生しない
        if (fillerCoroutine != null || fillerAudioSource.isPlaying)
        {
            Debug.Log("[SpeechController] Filler is already playing.");
            return;
        }

        fillerCoroutine = StartCoroutine(PlayOneRandomFiller());
        Debug.Log("[SpeechController] One filler started.");
    }

    private void StopFiller()
    {
        if (fillerCoroutine != null)
        {
            StopCoroutine(fillerCoroutine);
            fillerCoroutine = null;
        }

        if (fillerAudioSource != null)
        {
            fillerAudioSource.Stop();
            fillerAudioSource.clip = null;
        }

        Debug.Log("[SpeechController] Filler stopped.");
    }

    private IEnumerator PlayOneRandomFiller()
    {
        AudioClip selectedClip = GetRandomFillerClip();

        if (selectedClip == null)
        {
            Debug.LogWarning("[SpeechController] Selected filler clip is null.");
            fillerCoroutine = null;
            yield break;
        }

        if (selectedClip.loadState == AudioDataLoadState.Unloaded)
        {
            selectedClip.LoadAudioData();
        }

        while (selectedClip.loadState == AudioDataLoadState.Loading)
        {
            yield return null;
        }

        if (selectedClip.loadState != AudioDataLoadState.Loaded)
        {
            Debug.LogWarning("[SpeechController] Filler clip is not loaded: " + selectedClip.name + " / " + selectedClip.loadState);
            fillerCoroutine = null;
            yield break;
        }

        fillerAudioSource.Stop();
        fillerAudioSource.clip = selectedClip;
        fillerAudioSource.volume = fillerVolume;
        fillerAudioSource.Play();

        Debug.Log("[SpeechController] Playing one filler: " + selectedClip.name);

        while (fillerAudioSource != null && fillerAudioSource.isPlaying)
        {
            yield return null;
        }

        fillerCoroutine = null;
        Debug.Log("[SpeechController] Filler finished.");
    }

    private IEnumerator WaitForFillerToFinish()
    {
        if (fillerCoroutine != null)
        {
            Coroutine runningFiller = fillerCoroutine;
            yield return runningFiller;
        }

        if (fillerAudioSource != null)
        {
            while (fillerAudioSource.isPlaying)
            {
                yield return null;
            }
        }

        fillerCoroutine = null;
        Debug.Log("[SpeechController] Filler finished. AI can speak now.");
    }

    private AudioClip GetRandomFillerClip()
    {
        if (fillerClips == null || fillerClips.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < fillerClips.Length; i++)
        {
            int index = UnityEngine.Random.Range(0, fillerClips.Length);

            if (fillerClips[index] != null)
            {
                return fillerClips[index];
            }
        }

        return null;
    }

    private bool HasValidFillerClip()
    {
        if (fillerClips == null)
        {
            return false;
        }

        foreach (AudioClip clip in fillerClips)
        {
            if (clip != null)
            {
                return true;
            }
        }

        return false;
    }

    private void DeleteSpeechFile()
    {
        if (!string.IsNullOrEmpty(speechFilePath) && File.Exists(speechFilePath))
        {
            try
            {
                File.Delete(speechFilePath);
                Debug.Log("[SpeechController] Deleted speech file: " + speechFilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SpeechController] Could not delete speech file: " + e.Message);
            }
        }

        speechFilePath = "";
    }

    private string ExtractValue(string text, string pattern)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        Match match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : "";
    }

    public string GetResponseReply()
    {
        return currentReply;
    }

    public string GetResponseGesture()
    {
        return currentGesture;
    }

    public string GetSpeechFilePath()
    {
        return speechFilePath;
    }

    public class AsyncResult<T>
    {
        public T Result = default(T);
        public bool IsDone = false;
    }
}