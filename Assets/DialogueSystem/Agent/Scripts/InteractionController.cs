using Oculus.Voice;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using SD = System.Diagnostics;

public class InteractionController : MonoBehaviour
{
    [Header("UI Settings")]
    [SerializeField] private TextMeshProUGUI speechTextUI;
    private string speechText = "";

    [Header("Speech Settings")]
    [SerializeField] private AppVoiceExperience appVoiceExperience;
    [SerializeField] private SpeechController speechController;
    [SerializeField] private TextAsset replyPromptText = null;
    private string prompt = "ex. Please enjoy to talk with user.";

    [Header("Motion Settings")]
    [SerializeField] private MotionAPI motionAPI;
    [SerializeField] private MotionController motionController;
    [SerializeField] private TextAsset gesturePromptText = null;

    [Header("Emotion Settings")]
    private string userEmotion = "";
    private string aiEmotion = "";

    [Header("Interaction Settings")]
    [SerializeField][Range(0, 10)][Tooltip("If SaftyLimit is 0, it stands for unlimited mode.")] private int saftyLimit = 3;
    [SerializeField] private TextMeshProUGUI systemCondition = null;
    private int interactionCount = 0;
    private bool isInteracting = false;
    private bool isProccessing = false;

    [Header("Developping Settings")]
    [SerializeField] private DeveloppingToolsController developpingToolsController;

    private void Start()
    {
        if (developpingToolsController == null)
        {
            Debug.LogWarning("[InteractionController] Developping Tools Controller is not found");
        }
        else
        {
            developpingToolsController.AddLog("[InteractionController] Setting ...");
        }

        userEmotion = "Neutral";
        aiEmotion = "Neutral";
        
        if (developpingToolsController != null) developpingToolsController.ResetLog();

        if (speechController == null)
        {
            Debug.LogError("[InteractionController] SpeechController not found is scene!");
        }

        if (replyPromptText == null)
        {
            Debug.LogError("[InteractionController] PromptText is null!");
        }
        else
        {
            prompt = replyPromptText.text;
        }

        if (speechTextUI == null)
        {
            Debug.LogError("[InteractionController] UserTextUI not found is scene!");
        }
        else
        {
            speechTextUI.text = "";
        }
        //Voice to Text
        if (appVoiceExperience != null)
        {
            appVoiceExperience.VoiceEvents.OnRequestCompleted.AddListener(OnRequestCompleted);
            appVoiceExperience.VoiceEvents.OnFullTranscription.AddListener(OnFullTranscription);
            appVoiceExperience.Activate();
        }
        else
        {
            Debug.LogError("[InteractionController] AppVoiceExperience not found in this scene!");
        }
        if (systemCondition != null) 
        {
            systemCondition.text = "<color=green>Normal</color>";
        }
    }
    private void OnRequestCompleted()
    {
        appVoiceExperience.Activate();
    }

    private void OnFullTranscription(string transcription)
    {
        if (isInteracting && isProccessing)
        {
            Debug.Log($"[InteractionController] transcription: {transcription}");
            if (!string.IsNullOrEmpty(transcription))
            {
                interactionCount += 1;

                if (saftyLimit == 0 || saftyLimit > interactionCount)
                {
                    StartCoroutine(RunAllGenerativeAI(transcription));
                }
            }
        }
    }

    private IEnumerator RunAllGenerativeAI(string content)
    {
        isProccessing = false;
        Debug.Log("[InteractionController] Start to generate reply then gesture...");
        SD.Stopwatch totalSw = SD.Stopwatch.StartNew();

        string replyPrompt = CreateReplyPrompt(replyPromptText.text, speechText, content, userEmotion);
        string gesturePrompt = CreateGesturePrompt(gesturePromptText.text, speechText, aiEmotion, userEmotion);

        Coroutine replyCoroutine = StartCoroutine(speechController.GenerateReplyText(replyPrompt));
        Coroutine gestureCoroutine = StartCoroutine(speechController.GenerateGestureText(gesturePrompt));

        yield return replyCoroutine;
        yield return gestureCoroutine;

        string generatedReply = speechController.GetResponseReply();
        string gestureText = speechController.GetResponseGesture();

        speechText += $"User: {content} (k={interactionCount})\n";
        speechTextUI.text = $"User: {content}\nAI: {generatedReply}";
        speechText += $"AI: {generatedReply} (k={interactionCount})(gesture: {gestureText})\n";

        Coroutine speechCoroutine = StartCoroutine(speechController.GenerateSpeech());
        Coroutine motionCoroutine = StartCoroutine(motionAPI.GenerativeMotion(speechController.GetResponseGesture()));

        yield return speechCoroutine;
        yield return motionCoroutine;

        totalSw.Stop();

        string csvLine = $"{interactionCount}," +
                         $"\"{content.Replace("\"", "\"\"")}\"," +
                         $"\"{generatedReply.Replace("\"", "\"\"")}\"," +
                         $"\"{gestureText.Replace("\"", "\"\"")}\"," +
                         $"{totalSw.ElapsedMilliseconds},";

        developpingToolsController.AddLog(csvLine);

        motionController.SwitchMotion(motionAPI.PreProcessingMotionData(motionAPI.GetMotionData()));
        yield return StartCoroutine(speechController.PlayAudio());
        isProccessing = true;
    }

    public string CreateReplyPrompt(string template, string backInformation, string transaction, string userEmotion)
    {
        string prompt = template.Replace("[Transaction]", transaction);
        prompt = prompt.Replace("[UserEmotion]", userEmotion);
        prompt = prompt.Replace("[BackInformation]", backInformation);

        return prompt;
    }

    public string CreateGesturePrompt(string template, string backInformation, string aiEmotion, string userEmotion)
    {
        string p = template.Replace("[UserEmotion]", userEmotion);
        p = p.Replace("[AiEmotion]", aiEmotion);
        p = p.Replace("[BackInformation]", backInformation);        
        return p;
    }

    public void StartInteraction()
    {
        isInteracting = true;
        isProccessing = true;
        if (developpingToolsController != null)
        {
            systemCondition.text = "<color=green>Interacting...</color>";
        }
    }
    public void StopInteraction()
    {
        isInteracting = false;
        isProccessing = false;
        if (developpingToolsController != null)
        {
            systemCondition.text = "<color=red>Not Interacting...</color>";
        }
    }
    public void RestartInteraction()
    {
        isInteracting = true;
        isProccessing = true;

        userEmotion = "Neutral";
        aiEmotion = "Neutral";
        if (developpingToolsController != null) developpingToolsController.ResetLog();

        speechTextUI.text = "";

        if (developpingToolsController != null)
        {
            systemCondition.text = "<color=green>Interacting...</color>";
        }
    }

    public void SaveLog()
    {
        StartCoroutine(developpingToolsController.SaveLogCoroutine());
    }

    private void OnDestroy()
    {
        appVoiceExperience.VoiceEvents.OnRequestCompleted.RemoveListener(OnRequestCompleted);
        appVoiceExperience.VoiceEvents.OnFullTranscription.RemoveListener(OnFullTranscription);
    }
}
