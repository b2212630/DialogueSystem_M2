using Newtonsoft.Json;
using OVRSimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class MotionAPI : MonoBehaviour
{
    private string url = "";
    [SerializeField] private ApiResources apiResources;
    private string response = "";
    private MotionData motionData;

    private void Start()
    {
        url = apiResources.motionApiUrl;
    }

    [ContextMenu("Send Test Request")]
    public void RunGenerativeMotion(string prompt)
    {
        StartCoroutine(GenerativeMotion(prompt));
    }

    public IEnumerator GenerativeMotion(string text)
    {
        var json = "{\"text\":\"" + text + "\"}";
        var request = new UnityWebRequest(url, "POST");

        byte[] body = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string res = request.downloadHandler.text;

            try
            {
                response = res;
                motionData = ConvertJsonToMotionData(response);
                Debug.Log($"[MotionAPI] Converted MotionData(frame: {motionData.frames.Count})");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MotionAPI] Cannot convert json to MotionData\nError: {e}");
                response = e.ToString();
            }
        }
        else
        {
            Debug.LogError("[MotionAPI] Error: " + request.error);
            yield break;
        }
    }

    public MotionData ConvertJsonToMotionData(string json)
    {
        try
        {
            if (json.StartsWith("\"") && json.EndsWith("\""))
            {
                json = json.Substring(1, json.Length - 2);
            }

            json = json.Replace("\\\"", "\"");

            MotionData data = JsonConvert.DeserializeObject<MotionData>(json);

            if (data == null || data.frames == null)
            {
                Debug.LogError("[MotionController] Parsed MotionData is invalid.");
                throw new Exception();
            }

            return data;
        }
        catch (Exception e)
        {
            Debug.LogError($"[MotionController] JSON Parse Exception: {e}\nOriginal JSON: {json}");
            return null;
        }
    }

    public string ConvertMotionDataToJson(MotionAPI.MotionData data)
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
        };

        return JsonConvert.SerializeObject(data, settings);
    }

    #region Methods for processing MotionData
    public MotionData CombineWithInterpolation(MotionData motionA, MotionData motionB, int interpCount = 3)
    {
        MotionData result = new MotionData
        {
            frames = new List<FrameData>()
        };

        result.frames.AddRange(motionA.frames);

        if (motionA.frames.Count > 0 && motionB.frames.Count > 0)
        {
            FrameData lastA = motionA.frames[^1];
            FrameData firstB = motionB.frames[0];

            var bridge = CreateSmoothFrames(lastA, firstB, interpCount);
            result.frames.AddRange(bridge);
        }

        result.frames.AddRange(motionB.frames);

        return result;
    }

    public MotionData PreProcessingMotionData(MotionData raw)
    {
        MotionData cloned = CloneMotionData(raw);

        return AjustMotionDataPosition(cloned);
    }

    public MotionData PreProcessingMotionData(string raw)
    {
        MotionData cloned = CloneMotionData(ConvertJsonToMotionData(raw));

        return AjustMotionDataPosition(cloned);
    }

    public MotionData MakeLoopSmooth(MotionData preProcessed, int interpCount = 10)
    {
        MotionData cloned = CloneMotionData(preProcessed);

        if (cloned.frames.Count < 2) return cloned;

        var last = cloned.frames[^1];
        var first = cloned.frames[0];

        var bridge = CreateSmoothFrames(last, first, interpCount);
        cloned.frames.AddRange(bridge);

        return cloned;
    }

    private MotionData CloneMotionData(MotionData src)
    {
        MotionData dst = new MotionData
        {
            frames = new List<FrameData>()
        };

        foreach (var f in src.frames)
        {
            FrameData nf = new FrameData
            {
                bones = new Dictionary<string, BoneData>()
            };

            foreach (var b in f.bones)
            {
                nf.bones[b.Key] = new BoneData
                {
                    x = b.Value.x,
                    y = b.Value.y,
                    z = b.Value.z
                };
            }
            dst.frames.Add(nf);
        }
        return dst;
    }

    private List<FrameData> CreateSmoothFrames(FrameData start, FrameData end, int count)
    {
        List<FrameData> frames = new List<FrameData>();

        for (int i = 1; i <= count; i++)
        {
            float t = (float)i / (count + 1);

            float smoothedT = t * t * (3f - 2f * t);

            FrameData newFrame = new FrameData();
            newFrame.bones = new Dictionary<string, BoneData>();

            foreach (var kvp in start.bones)
            {
                string boneName = kvp.Key;
                if (end.bones.TryGetValue(boneName, out BoneData boneB))
                {
                    BoneData boneA = kvp.Value;

                    BoneData lerpedBone = new BoneData
                    {
                        x = Mathf.Lerp(boneA.x, boneB.x, smoothedT),
                        y = Mathf.Lerp(boneA.y, boneB.y, smoothedT),
                        z = Mathf.Lerp(boneA.z, boneB.z, smoothedT)
                    };
                    newFrame.bones.Add(boneName, lerpedBone);
                }
            }
            frames.Add(newFrame);
        }
        return frames;
    }

    public MotionData AjustMotionDataPosition(MotionData data)
    {
        if (data == null || data.frames == null || data.frames.Count < 2) return null;

        if (!data.frames[0].bones.ContainsKey("Hips") ||
            !data.frames[data.frames.Count - 1].bones.ContainsKey("Hips")) return null;

        BoneData startHips = data.frames[0].bones["Hips"];
        BoneData endHips = data.frames[data.frames.Count - 1].bones["Hips"];

        float driftX = endHips.x - startHips.x;
        float driftY = endHips.y - startHips.y;
        float driftZ = endHips.z - startHips.z;

        for (int i = 0; i < data.frames.Count; i++)
        {
            float t = (float)i / (data.frames.Count - 1);

            data.frames[i].bones["Hips"].x -= driftX * t;
            data.frames[i].bones["Hips"].y -= driftY * t;
            data.frames[i].bones["Hips"].z -= driftZ * t;
        }
        return data;
    }
    #endregion

    // Data Class
    public string GetResponse() { return response; }

    public MotionData GetMotionData() { return  motionData; }

    [System.Serializable]
    public class MotionData
    {
        public List<FrameData> frames { get; set; }
    }

    [System.Serializable]
    public class FrameData
    {
        public Dictionary<string, BoneData> bones { get; set; }
    }

    [System.Serializable]
    public class BoneData
    {
        public float x;
        public float y;
        public float z;

        public Vector3 GetBoneDataForUnity()
        {
            return new Vector3(x, y, -z);
        }
        public Vector3 GetBoneData()
        {
            return new Vector3(x, y, z);
        }
    }
}
