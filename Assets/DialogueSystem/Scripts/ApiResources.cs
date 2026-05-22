using UnityEngine;

[CreateAssetMenu(fileName = "ApiKeys", menuName = "API Resources/Create API Resources Asset")]
public class ApiResources : ScriptableObject
{
    [Header("OpenAI API Resources")]
    [Tooltip("Write Your OpenAI API Key.")]
    public string openAIApiKey;

    [Header("T2M-GPT API Resources")]
    [Tooltip("Write Your API URL")]
    public string motionApiUrl;

    [Header("Emotional Prediction API Resources")]
    [Tooltip("Write Your API URL")]
    public string emotionApiUrl;

    [Header("Log API Resources")]
    [Tooltip("Write Your API URL")]
    public string logApiUrl;
}