using System;
using System.Collections.Generic;
using UnityEngine;

public class InstructionPlayback : MonoBehaviour
{
    public enum PlaybackMode
    {
        Global,
        Local
    }

    public bool disableRotation;
    [Range(0.1f, 1.0f)] public float translationScale = 1.0f;
    public PlaybackMode playbackMode = PlaybackMode.Global;
    public GameObject referenceObject;
    [SerializeField] private TextAsset instructionJson;
    [SerializeField] private List<TrackedFrame> frames = new List<TrackedFrame>();
    [SerializeField] private List<ClassMeshMapping> classMeshMappings = new List<ClassMeshMapping>();
    [SerializeField] private GameObject cameraPlaceholderPrefab;
    [SerializeField] private float frameRate = 12f;

    private readonly Dictionary<string, List<GameObject>> objectsByClass = new Dictionary<string, List<GameObject>>();
    private GameObject trackedCameraPlaceholder;
    private float frameTimer;
    private int frameIndex;

    private void Start()
    {
        if (instructionJson != null)
        {
            frames = JSONInstructionParser.Parse(instructionJson.text);
        }

        InstantiateCameraPlaceholder();
        InstantiateClassObjects();

        DisplayFrame(frameIndex);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            playbackMode = playbackMode == PlaybackMode.Global ? PlaybackMode.Local : PlaybackMode.Global;
            DisplayFrame(frameIndex);
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
        DisplayFrame(frameIndex);
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

    private void DisplayFrame(int frameIndex)
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
            trackedCameraPlaceholder.transform.localPosition = frame.cameraTranslation;
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
                instance.transform.localPosition = meshData.translation * translationScale;
                instance.transform.localRotation = disableRotation ? Quaternion.identity : meshData.rotation;
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

    [Serializable]
    public class ClassMeshMapping
    {
        public string className;
        public List<GameObject> meshPrefabs = new List<GameObject>();
    }
}