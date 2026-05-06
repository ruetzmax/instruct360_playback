using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus;
using UnityEngine.Video;

public class InstructionPlayback : MonoBehaviour
{
    public enum PlaybackMode
    {
        Global,
        Local
    }

    public bool disableRotation;
    [Range(0.1f, 1.0f)] public float translationScale = 1.0f;
    [Range(-5.0f, 5.0f)] public float translationYOffset = 0.0f;
    public bool enableVideoStabilization = true;
    [Range(-60, 60)] public int videoRotationFrameOffset = 0;
    public PlaybackMode playbackMode = PlaybackMode.Global;
    public bool displayVideo = false;
    public VideoClip videoClip;
    public GameObject referenceObject;
    public OVRPassthroughLayer passthroughLayer;
    public OVRHand rightHand;
    public OVRHand leftHand;
    [SerializeField] private TextAsset instructionJson;
    [SerializeField] private List<TrackedFrame> frames = new List<TrackedFrame>();
    [SerializeField] private List<ClassMeshMapping> classMeshMappings = new List<ClassMeshMapping>();
    [SerializeField] private GameObject cameraPlaceholderPrefab;
    [SerializeField] private float frameRate = 12f;
    public bool displayObjectTrails = true;
    public Material trailMaterial;

    private readonly Dictionary<string, List<GameObject>> objectsByClass = new Dictionary<string, List<GameObject>>();
    private GameObject trackedCameraPlaceholder;
    private float frameTimer;
    private int frameIndex;
    private Camera mainCamera;
    private VideoPlayer videoPlayer;
    private bool lastDisplayVideoState;

    //used only when duplicating video frames to introduce shift (for stabilization)
    private Shader renderTextureShiftShader;
    private Material renderTextureShiftMaterial;

    private void Start()
    {
        mainCamera = Camera.main;
        videoPlayer = GetComponent<VideoPlayer>();
        videoPlayer.clip = videoClip;
        videoPlayer.Prepare();
        videoPlayer.Pause();
        videoPlayer.sendFrameReadyEvents = true;
        videoPlayer.frameReady += OnVideoFrameReady;

        renderTextureShiftShader = Shader.Find("Hidden/RenderTextureShiftWrap");
        renderTextureShiftMaterial = new Material(renderTextureShiftShader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };


        lastDisplayVideoState = !displayVideo;
        ApplyDisplayVideoState(force: true);

        if (instructionJson != null)
        {
            frames = JSONInstructionParser.Parse(instructionJson.text);
        }

        InstantiateCameraPlaceholder();
        InstantiateClassObjects();
        SetupObjectTrails();

        DisplayObjectsForFrame(frameIndex);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.V) || rightHand.IsReleased())
        {
            displayVideo = !displayVideo;
        }
        ApplyDisplayVideoState();

        if (Input.GetKeyDown(KeyCode.Space) || leftHand.IsReleased())
        {
            playbackMode = playbackMode == PlaybackMode.Global ? PlaybackMode.Local : PlaybackMode.Global;
            DisplayObjectsForFrame(frameIndex);
        }

        if (frames.Count == 0)
        {
            return;
        }

        frameTimer += Time.deltaTime;
        float frameDuration = frameRate > 0f ? 1f / frameRate : 0f;

        if (frameDuration <= 0f || frameTimer < frameDuration)
        {
            return;
        }

        frameTimer -= frameDuration;
        frameIndex = (frameIndex + 1) % frames.Count;
        DisplayFrame(frameIndex, updateObjects: !displayVideo);
    }

    private void SetupObjectTrails()
    {
        foreach (KeyValuePair<string, List<GameObject>> entry in objectsByClass)
        {
            foreach (GameObject obj in entry.Value)
            {
                TrailRenderer tr = obj.AddComponent<TrailRenderer>();
                tr.startWidth = 0.03f;
                tr.endWidth = 0.01f;
                tr.startColor = new Color(0, 189f/255f, 1);
                tr.endColor = new Color(0, 26f/255f, 1);
                tr.material = trailMaterial;
                tr.time = 3f;
                tr.enabled = displayObjectTrails;
            }
        }
    }

    private void UpdateObjectTrails(bool reset = false)
    {
        foreach (KeyValuePair<string, List<GameObject>> entry in objectsByClass)
        {
            foreach (GameObject obj in entry.Value)
            {
                TrailRenderer tr = obj.GetComponent<TrailRenderer>();
                if (frameIndex < 3)
                {
                    tr.Clear();
                }
                tr.AddPosition(obj.transform.position);
                tr.enabled = displayObjectTrails;
            }
        } 
    }

    private void ApplyDisplayVideoState(bool force = false)
    {
        if (!force && displayVideo == lastDisplayVideoState)
        {
            return;
        }

        lastDisplayVideoState = displayVideo;

        if (displayVideo)
        {
            mainCamera.clearFlags = CameraClearFlags.Skybox;

            if (passthroughLayer != null)
            {
                passthroughLayer.enabled = false;
            }
        }

        else
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);

            if (passthroughLayer != null)
            {
                passthroughLayer.enabled = true;
            }
        }
    }

    private void ShiftVideoTargetTexture(Quaternion cameraRotation)
    {
        if (!enableVideoStabilization)
        {
            return;
        }

        float yaw = cameraRotation.eulerAngles.y;
        float yawOffset = -yaw / 360.0f;

        float pitch = Mathf.DeltaAngle(0.0f, cameraRotation.eulerAngles.x);
        float pitchOffset = -pitch / 180.0f;

        Vector2 offset = new Vector2(Mathf.Repeat(yawOffset, 1.0f), Mathf.Repeat(pitchOffset, 1.0f));
        renderTextureShiftMaterial.SetVector("_Offset", offset);

        RenderTexture target = videoPlayer.targetTexture;
        RenderTexture temp = RenderTexture.GetTemporary(target.descriptor);
        Graphics.Blit(target, temp, renderTextureShiftMaterial);
        Graphics.Blit(temp, target);
        RenderTexture.ReleaseTemporary(temp);
    }

    private void InstantiateClassObjects()
    {
        objectsByClass.Clear();

        foreach (ClassMeshMapping mapping in classMeshMappings)
        {
            var instances = new List<GameObject>(mapping.meshPrefabs.Count);
            for (int i = 0; i < mapping.meshPrefabs.Count; i++)
            {
                GameObject prefab = mapping.meshPrefabs[i];
                if (prefab == null)
                {
                    instances.Add(null);
                    continue;
                }

                GameObject instance = Instantiate(prefab, GetParentTransform());
                instance.name = $"{mapping.className}_{i}";
                instance.SetActive(false);
                instances.Add(instance);
            }

            objectsByClass[mapping.className] = instances;
        }
    }

    private void InstantiateCameraPlaceholder()
    {
        if (cameraPlaceholderPrefab == null)
        {
            return;
        }

        trackedCameraPlaceholder = Instantiate(cameraPlaceholderPrefab);
        trackedCameraPlaceholder.transform.localPosition = Vector3.zero;
        trackedCameraPlaceholder.transform.localRotation = Quaternion.identity;
        trackedCameraPlaceholder.name = "CameraPlaceholder";
    }

    private void DisplayFrame(int frameIndex, bool updateObjects)
    {
        if (frameIndex < 0 || frameIndex >= frames.Count)
        {
            return;
        }


        // update video player frame
        if (displayVideo)
        {
            videoPlayer.frame = frameIndex;
            videoPlayer.StepForward();
        }

        if (updateObjects)
        {
            DisplayObjectsForFrame(frameIndex);
            UpdateObjectTrails();
        }
    }

    private void DisplayObjectsForFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= frames.Count)
        {
            return;
        }

        TrackedFrame frame = frames[frameIndex];

        // update tracked camera placeholder
        trackedCameraPlaceholder.SetActive(false);
        if (trackedCameraPlaceholder != null && playbackMode == PlaybackMode.Global)
        {
            trackedCameraPlaceholder.SetActive(true);
            Vector3 cameraTranslation = frame.cameraTranslation;
            cameraTranslation.y = -cameraTranslation.y;
            trackedCameraPlaceholder.transform.localPosition = cameraTranslation;
            trackedCameraPlaceholder.transform.localRotation = frame.cameraRotation;
        }

        // hide all objects
        foreach (KeyValuePair<string, List<GameObject>> entry in objectsByClass)
        {
            foreach (GameObject obj in entry.Value)
            {
                if (obj != null)
                {
                    obj.SetActive(false);
                }
            }
        }

        if (frame.classes == null)
        {
            return;
        }

        // iterate over classes, display meshes accordingly
        foreach (TrackedClass trackedClass in frame.classes)
        {
            if (!objectsByClass.TryGetValue(trackedClass.className, out List<GameObject> objects))
            {
                continue;
            }

            if (trackedClass.reconstructedMeshes == null)
            {
                continue;
            }

            for (int reconstructedObjIndex = 0; reconstructedObjIndex < trackedClass.reconstructedMeshes.Count; reconstructedObjIndex++)
            {
                if (reconstructedObjIndex >= objects.Count)
                {
                    break;
                }

                GameObject instance = objects[reconstructedObjIndex];
                if (instance == null)
                {
                    continue;
                }

                ReconstructedMesh meshData = trackedClass.reconstructedMeshes[reconstructedObjIndex];
                Vector3 scaledTranslation = meshData.translation * translationScale;
                Quaternion objectRotation = disableRotation ? Quaternion.identity : meshData.rotation;

                if (playbackMode == PlaybackMode.Local && enableVideoStabilization)
                {
                    //adjust translation/rotation based on camera rotation (for stabilization)
                    scaledTranslation = Quaternion.Inverse(frame.cameraRotation) * scaledTranslation;
                    objectRotation = frame.cameraRotation * objectRotation;
                }

                scaledTranslation.y = -scaledTranslation.y + translationYOffset;
                instance.transform.localPosition = scaledTranslation;
                instance.transform.localRotation = objectRotation;
                instance.transform.localScale = meshData.scale;
                instance.SetActive(true);
                instance.transform.SetParent(GetParentTransform(), worldPositionStays: false);
            }
        }
    }

    private Transform GetParentTransform()
    {
        if (playbackMode == PlaybackMode.Global && trackedCameraPlaceholder != null)
        {
            return trackedCameraPlaceholder.transform;
        }

        if (referenceObject != null)
        {
            return referenceObject.transform;
        }

        return transform;
    }

    private void OnVideoFrameReady(VideoPlayer source, long frameIdx)
    {
        if (displayVideo && enableVideoStabilization)
        {
            ShiftVideoTargetTexture(GetRotationForVideoFrame(frameIdx));
        }

        if (displayVideo)
        {
            int normalizedIndex = NormalizeFrameIndex(frameIdx);
            frameIndex = normalizedIndex;
            DisplayObjectsForFrame(normalizedIndex);
        }
    }

    private int NormalizeFrameIndex(long frameIdx)
    {
        int count = frames.Count;
        if (count <= 0)
        {
            return 0;
        }

        int index = (int)(frameIdx % count);
        if (index < 0)
        {
            index += count;
        }

        return index;
    }

    private Quaternion GetRotationForVideoFrame(long frameIdx)
    {
        int count = frames.Count;
        int baseIndex = NormalizeFrameIndex(frameIdx);

        int offsetIndex = baseIndex + videoRotationFrameOffset;
        offsetIndex %= count;
        if (offsetIndex < 0)
        {
            offsetIndex += count;
        }

        return frames[offsetIndex].cameraRotation;
    }

    private void OnDisable()
    {
        if (videoPlayer != null)
        {
            videoPlayer.frameReady -= OnVideoFrameReady;
        }
    }

    [Serializable]
    public class ClassMeshMapping
    {
        public string className;
        public List<GameObject> meshPrefabs = new List<GameObject>();
    }
}