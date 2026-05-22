using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;
using static MotionAPI;

public class MotionController : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private float frameRate = 30.0f;
    [SerializeField] private float playbackSpeed = 1.0f;
    [SerializeField] private bool loop = true;
    [SerializeField] private float positionScale = 1.0f; // Ajust the scale for MotionData
    [SerializeField] private MotionAPI motionAPI;
    [SerializeField] private float neckPitchOffset = 15f;
    [SerializeField] private float headPitchOffset = 10f;
    [SerializeField] private List<TextAsset> idleMotionDataTexts;

    [Header("Developping Settings")]
    [SerializeField] private string idleMotionDataTextsPath = "Assets/Resources/IdleMotions";

    #region Local Variable

    private float currentTime = 0f;
    private bool isPlaying = false;
    private Dictionary<string, Transform> boneMap;
    private MotionData currentMotionData;
    private MotionData idleMotionData;
    private List<MotionData> idleMotionDatas;

    // Caching initial pose information
    private Dictionary<string, Vector3> initialLocalDir = new Dictionary<string, Vector3>(); // Direction vector to child bone in parent space
    private Dictionary<HumanBodyBones, Quaternion> initialLocalRotations = new Dictionary<HumanBodyBones, Quaternion>(); // Initial local rotation of each bone (for twist reset)

    // For correcting the initial state of Hips
    private Quaternion initialHipsRotation;
    private Quaternion initialHipsBasisInverse; // Difference between the reference rotation in the data and Unity bone rotation
    private Vector3 initialHipsPosition;
    private Vector3 dataHipsStartPosition;
    private Vector3 currentWorldReferencePosition;

    private List<BoneConnection> boneConnections;

    #region Experiments

    #endregion

    #endregion

    void CacheInitialPose()
    {
        // 1. Cache the vectors and rotations of each joint
        foreach (var conn in boneConnections)
        {
            Transform p = targetAnimator.GetBoneTransform(conn.parentBone);
            Transform c = targetAnimator.GetBoneTransform(conn.childBone);
            if (p == null || c == null) continue;

            // Direction vector of the child from the parent's perspective (local)
            initialLocalDir[conn.parentKey] = p.InverseTransformPoint(c.position).normalized;
            initialLocalRotations[conn.parentBone] = p.localRotation;


            // Save Initial Local Rotation
            if (!initialLocalRotations.ContainsKey(conn.parentBone))
            {
                initialLocalRotations[conn.parentBone] = p.localRotation;
            }
        }

        // 2. Cache the initial state of Hips
        Transform hips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips != null)
        {
            initialHipsPosition = hips.position;
        }

        Transform spine = targetAnimator.GetBoneTransform(HumanBodyBones.Spine);
        Transform leftLeg = targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rightLeg = targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);

        if (hips != null)
        {
            initialLocalRotations[HumanBodyBones.Hips] = hips.localRotation;
            initialHipsRotation = hips.rotation;

            // Calculate the basis rotation of the Hips and maintain the offset with Unity's Hips rotation.
            if (spine != null && leftLeg != null && rightLeg != null)
            {
                Vector3 up = (spine.position - hips.position).normalized;
                Vector3 right = (rightLeg.position - leftLeg.position).normalized;
                Vector3 forward = Vector3.Cross(right, up).normalized;

                // Recalculate the exact right (orthogonalization)
                Vector3 orthoRight = Vector3.Cross(up, forward).normalized;

                if (forward != Vector3.zero && up != Vector3.zero)
                {
                    Quaternion initialBasis = Quaternion.LookRotation(forward, up);
                    initialHipsBasisInverse = Quaternion.Inverse(initialBasis) * hips.rotation;
                }
                else
                {
                    initialHipsBasisInverse = Quaternion.identity;
                }
            }
        }
    }

    private class BoneConnection
    {
        public string parentKey;
        public string childKey;
        public HumanBodyBones parentBone;
        public HumanBodyBones childBone;

        public BoneConnection(string pKey, string cKey, HumanBodyBones pBone, HumanBodyBones cBone)
        {
            parentKey = pKey;
            childKey = cKey;
            parentBone = pBone;
            childBone = cBone;
        }
    }

    void Start()
    {
        if (targetAnimator == null) targetAnimator = GetComponent<Animator>();

        InitializeBoneMap();
        DefineConnections();
        CacheInitialPose();

        LoadIdleMotionData();
        RandomSelectIdleMotionData();

        currentWorldReferencePosition = initialHipsPosition;
        currentMotionData = idleMotionData;

        Gesture(idleMotionData);
    }

    private void LoadIdleMotionData()
    {
        idleMotionDatas = new List<MotionData>();
        if (idleMotionDataTexts.Count == 0)
        {
            Debug.LogError($"[MotionController] IdleMotionDataTexts is null");
        }
        else
        {
            foreach (TextAsset imdt in idleMotionDataTexts)
            {
                idleMotionDatas.Add(motionAPI.PreProcessingMotionData(imdt.text));
            }
        }
    }

    private void RandomSelectIdleMotionData()
    {
        idleMotionData = idleMotionDatas[Random.Range(0, idleMotionDatas.Count)];
    }

    public void Gesture(MotionData motionData)
    {
        currentMotionData = motionData;
        Play();
    }

    void InitializeBoneMap()
    {
        boneMap = new Dictionary<string, Transform>();
        var keyToHuman = new Dictionary<string, HumanBodyBones>()
        {
            { "Hips", HumanBodyBones.Hips },
            { "Spine", HumanBodyBones.Spine },
            { "Chest", HumanBodyBones.Chest },
            { "Neck", HumanBodyBones.Neck },
            { "Head", HumanBodyBones.Head },
            { "LeftShoulder", HumanBodyBones.LeftShoulder },
            { "LeftUpperArm", HumanBodyBones.LeftUpperArm },
            { "LeftLowerArm", HumanBodyBones.LeftLowerArm },
            { "LeftHand", HumanBodyBones.LeftHand },
            { "RightShoulder", HumanBodyBones.RightShoulder },
            { "RightUpperArm", HumanBodyBones.RightUpperArm },
            { "RightLowerArm", HumanBodyBones.RightLowerArm },
            { "RightHand", HumanBodyBones.RightHand },
            { "LeftUpperLeg", HumanBodyBones.LeftUpperLeg },
            { "LeftLowerLeg", HumanBodyBones.LeftLowerLeg },
            { "LeftFoot", HumanBodyBones.LeftFoot },
            { "RightUpperLeg", HumanBodyBones.RightUpperLeg },
            { "RightLowerLeg", HumanBodyBones.RightLowerLeg },
            { "RightFoot", HumanBodyBones.RightFoot }
        };

        foreach (var kvp in keyToHuman)
        {
            Transform t = targetAnimator.GetBoneTransform(kvp.Value);
            if (t != null) boneMap[kvp.Key] = t;
        }
    }

    void DefineConnections()
    {
        boneConnections = new List<BoneConnection>
        {
            new BoneConnection("Spine", "Chest", HumanBodyBones.Spine, HumanBodyBones.Chest),
            new BoneConnection("Chest", "Neck", HumanBodyBones.Chest, HumanBodyBones.Neck),
            new BoneConnection("Neck", "Head", HumanBodyBones.Neck, HumanBodyBones.Head),
            new BoneConnection("LeftShoulder", "LeftUpperArm", HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm),
            new BoneConnection("LeftUpperArm", "LeftLowerArm", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm),
            new BoneConnection("LeftLowerArm", "LeftHand", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
            new BoneConnection("RightShoulder", "RightUpperArm", HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm),
            new BoneConnection("RightUpperArm", "RightLowerArm", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm),
            new BoneConnection("RightLowerArm", "RightHand", HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
            new BoneConnection("LeftUpperLeg", "LeftLowerLeg", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg),
            new BoneConnection("LeftLowerLeg", "LeftFoot", HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
            new BoneConnection("RightUpperLeg", "RightLowerLeg", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg),
            new BoneConnection("RightLowerLeg", "RightFoot", HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot)
        };
    }

    public void Play()
    {
        currentTime = 0f;
        isPlaying = true;
        targetAnimator.enabled = false;

        if (currentMotionData != null && currentMotionData.frames.Count > 0)
        {
            dataHipsStartPosition =
                currentMotionData.frames[0].bones["Hips"].GetBoneDataForUnity();
        }
    }

    void LateUpdate()
    {
        if (!isPlaying || currentMotionData == null || currentMotionData.frames.Count == 0) return;

        currentTime += Time.deltaTime * frameRate * playbackSpeed;
        float totalFrames = currentMotionData.frames.Count;

        if (currentTime >= totalFrames - 1)
        {
            if (loop)
            {
                currentTime = 0f;
            }
            else
            {
                isPlaying = false;
                RandomSelectIdleMotionData();
                SwitchMotion(idleMotionData);
                return;
            }
        }

        int idxA = Mathf.FloorToInt(currentTime);
        int idxB = Mathf.Min(idxA + 1, (int)totalFrames - 1);
        float t = currentTime - idxA;

        ApplyRotationPose(idxA, idxB, t);
    }

    void ApplyRotationPose(int idxA, int idxB, float t)
    {
        FrameData frameA = currentMotionData.frames[idxA];
        FrameData frameB = currentMotionData.frames[idxB];

        ApplyHips(frameA, frameB, t);

        foreach (var conn in boneConnections)
        {
            if (frameA.bones.ContainsKey(conn.parentKey) && frameA.bones.ContainsKey(conn.childKey) &&
                frameB.bones.ContainsKey(conn.parentKey) && frameB.bones.ContainsKey(conn.childKey))
            {
                Transform boneTransform = targetAnimator.GetBoneTransform(conn.parentBone);

                if (boneTransform != null)
                {
                    // Calculate the target direction vector from the data (equivalent to World Space)
                    Vector3 posParentA = frameA.bones[conn.parentKey].GetBoneDataForUnity();
                    Vector3 posChildA = frameA.bones[conn.childKey].GetBoneDataForUnity();
                    Vector3 dirA = (posChildA - posParentA).normalized;

                    Vector3 posParentB = frameB.bones[conn.parentKey].GetBoneDataForUnity();
                    Vector3 posChildB = frameB.bones[conn.childKey].GetBoneDataForUnity();
                    Vector3 dirB = (posChildB - posParentB).normalized;

                    Vector3 targetDir = Vector3.Slerp(dirA, dirB, t);

                    // 1. Reset the bone's local rotation to its initial value (to eliminate twist)
                    if (initialLocalRotations.ContainsKey(conn.parentBone))
                    {
                        boneTransform.localRotation = initialLocalRotations[conn.parentBone];
                    }

                    // 2. Get the direction the bone is facing (World Space) using "Initial Rotation State"
                    // Because initialLocalDir is a vector in the parent local space, it is world-mapped using the TransformDirection of the current bone (parent).
                    Vector3 currentRestDir = boneTransform.TransformDirection(initialLocalDir[conn.parentKey]);

                    // 3. Calculate the rotation difference from the initial direction (currentRestDir) to the target direction (targetDir)
                    Quaternion rotDiff = Quaternion.FromToRotation(currentRestDir, targetDir);

                    // 4. Apply the difference to the initial rotation
                    //boneTransform.rotation = rotDiff * boneTransform.rotation;

                    // Experiment
                    Quaternion targetRotation = rotDiff * boneTransform.rotation;

                    if (conn.parentKey == "Neck")
                    {
                        targetRotation *= Quaternion.Euler(-neckPitchOffset, 0f, 0f);
                    }
                    else if (conn.parentKey == "Head")
                    {
                        targetRotation *= Quaternion.Euler(-headPitchOffset, 0f, 0f);
                    }

                    boneTransform.rotation = targetRotation;
                }
            }
        }
    }

    void ApplyHips(FrameData frameA, FrameData frameB, float t)
    {
        if (!frameA.bones.ContainsKey("Hips") || !frameB.bones.ContainsKey("Hips")) return;

        Transform hips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null) return;

        Vector3 posA = frameA.bones["Hips"].GetBoneDataForUnity();
        Vector3 posB = frameB.bones["Hips"].GetBoneDataForUnity();
        Vector3 finalPos = Vector3.Lerp(posA, posB, t) * positionScale;

        Vector3 delta = finalPos - (dataHipsStartPosition * positionScale);

        hips.position = currentWorldReferencePosition + delta;

        // Determine the hip direction using Spine, LeftUpperLeg, and RightUpperLeg.
        if (frameA.bones.ContainsKey("Spine") &&
            frameA.bones.ContainsKey("LeftUpperLeg") &&
            frameA.bones.ContainsKey("RightUpperLeg"))
        {
            // Basis of Frame A
            Vector3 spineA = frameA.bones["Spine"].GetBoneDataForUnity();
            Vector3 lLegA = frameA.bones["LeftUpperLeg"].GetBoneDataForUnity();
            Vector3 rLegA = frameA.bones["RightUpperLeg"].GetBoneDataForUnity();

            // Basis of Frame B
            Vector3 spineB = frameB.bones["Spine"].GetBoneDataForUnity();
            Vector3 lLegB = frameB.bones["LeftUpperLeg"].GetBoneDataForUnity();
            Vector3 rLegB = frameB.bones["RightUpperLeg"].GetBoneDataForUnity();

            // Interpolation
            Vector3 spineP = Vector3.Lerp(spineA, spineB, t);
            Vector3 lLegP = Vector3.Lerp(lLegA, lLegB, t);
            Vector3 rLegP = Vector3.Lerp(rLegA, rLegB, t);

            // Building a target basis
            Vector3 targetUp = (spineP - finalPos).normalized;
            Vector3 targetRight = (rLegP - lLegP).normalized;
            Vector3 targetForward = Vector3.Cross(targetRight, targetUp).normalized;

            if (targetForward != Vector3.zero && targetUp != Vector3.zero)
            {
                // Posture rotation on data
                Quaternion targetBasis = Quaternion.LookRotation(targetForward, targetUp);

                // Apply Unity's initial pose offset to determine final rotation (Specify absolute rotation without cumulative error)
                hips.rotation = targetBasis * initialHipsBasisInverse;
            }
        }
        else
        {
            // Fallback in case of missing bar data
            if (frameA.bones.ContainsKey("Spine"))
            {
                // Reset
                if (initialLocalRotations.ContainsKey(HumanBodyBones.Hips))
                    hips.localRotation = initialLocalRotations[HumanBodyBones.Hips];

                Vector3 spineA = frameA.bones["Spine"].GetBoneDataForUnity();
                Vector3 spineB = frameB.bones["Spine"].GetBoneDataForUnity();
                Vector3 targetUp = Vector3.Slerp((spineA - posA).normalized, (spineB - posB).normalized, t);

                // Up vector after reset
                Transform spine = targetAnimator.GetBoneTransform(HumanBodyBones.Spine);
                Vector3 currentUp = (spine.position - hips.position).normalized;

                Quaternion rotDiff = Quaternion.FromToRotation(currentUp, targetUp);
                hips.rotation = rotDiff * hips.rotation;
            }
        }
    }

    public void SwitchMotion(MotionData nextMotion)
    {
        if (currentMotionData == null)
        {
            currentMotionData = nextMotion;
            currentTime = 0f;
            isPlaying = true;
            return;
        }

        int currentIdx = Mathf.FloorToInt(currentTime);
        currentIdx = Mathf.Clamp(currentIdx, 0, currentMotionData.frames.Count - 1);

        MotionData currentSegment = new MotionData
        {
            frames = new List<FrameData>
        {
            currentMotionData.frames[currentIdx]
        }
        };

        MotionData combined =
            motionAPI.CombineWithInterpolation(
                currentSegment,
                nextMotion,
                interpCount: 5
            );

        currentMotionData = combined;
        currentTime = 0f;
        isPlaying = true;

        dataHipsStartPosition =
            currentMotionData.frames[0].bones["Hips"].GetBoneDataForUnity();
    }


#if UNITY_EDITOR
    public void LoadJsonFromFolder()
    {
        if (!AssetDatabase.IsValidFolder(idleMotionDataTextsPath))
        {
            Debug.LogError($"[MotionController] Not found folder: {idleMotionDataTextsPath}");
            return;
        }

        idleMotionDataTexts.Clear();

        string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { idleMotionDataTextsPath });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (Path.GetExtension(path).Equals(".json", System.StringComparison.OrdinalIgnoreCase))
            {
                TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset != null)
                {
                    idleMotionDataTexts.Add(asset);
                }
            }
        }

        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();

        Debug.Log($"[MotionController] Read and Set {idleMotionDataTexts.Count} files");
    }

    [CustomEditor(typeof(MotionController))]
    public class MotionDataManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            MotionController script = (MotionController)target;

            EditorGUILayout.Space();
            if (GUILayout.Button("Auto Set Json Files"))
            {
                script.LoadJsonFromFolder();
            }
        }
    }
#endif
}