using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

[Serializable]
public class TrackedFrame
{
    public Vector3 cameraTranslation;
    public Quaternion cameraRotation;
    public List<TrackedClass> classes;
}

[Serializable]
public class TrackedClass
{
    public string className;
    public List<ReconstructedMesh> reconstructedMeshes;
}

[Serializable]
public class ReconstructedMesh
{
    public Vector3 scale;
    public Quaternion rotation;
    public Vector3 translation;
    public Quaternion chunkRelativeRotation;
    public Vector3 chunkRelativeTranslation;
}

public static class JSONInstructionParser
{
    public static List<TrackedFrame> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<TrackedFrame>();
        }

        var frameDtos = JsonConvert.DeserializeObject<List<TrackedFrameDto>>(json);
        if (frameDtos == null)
        {
            return new List<TrackedFrame>();
        }

        var frames = new List<TrackedFrame>(frameDtos.Count);
        foreach (TrackedFrameDto frameDto in frameDtos)
        {
            frames.Add(ToTrackedFrame(frameDto));
        }

        return frames;
    }

    private static TrackedFrame ToTrackedFrame(TrackedFrameDto dto)
    {
        var frame = new TrackedFrame
        {
            cameraTranslation = ToVector3(dto.CameraTranslation),
            cameraRotation = ToQuaternion(dto.CameraRotation),
            classes = new List<TrackedClass>(dto.Classes?.Count ?? 0)
        };

        if (dto.Classes != null)
        {
            foreach (TrackedClassDto classDto in dto.Classes)
            {
                frame.classes.Add(ToTrackedClass(classDto));
            }
        }

        return frame;
    }

    private static TrackedClass ToTrackedClass(TrackedClassDto dto)
    {
        var trackedClass = new TrackedClass
        {
            className = dto.ClassName ?? string.Empty,
            reconstructedMeshes = new List<ReconstructedMesh>(dto.ReconstructedMeshes?.Count ?? 0)
        };

        if (dto.ReconstructedMeshes != null)
        {
            foreach (ReconstructedMeshDto meshDto in dto.ReconstructedMeshes)
            {
                trackedClass.reconstructedMeshes.Add(ToReconstructedMesh(meshDto));
            }
        }

        return trackedClass;
    }

    private static ReconstructedMesh ToReconstructedMesh(ReconstructedMeshDto dto)
    {
        return new ReconstructedMesh
        {
            scale = ToVector3(dto.Scale),
            rotation = ToQuaternion(dto.Rotation),
            translation = ToVector3(dto.Translation),
            chunkRelativeRotation = ToQuaternion(dto.ChunkRelativeRotation),
            chunkRelativeTranslation = ToVector3(dto.ChunkRelativeTranslation)
        };
    }

    private static Vector3 ToVector3(float[] values)
    {
        if (values == null || values.Length != 3)
        {
            return Vector3.zero;
        }

        return new Vector3(values[0], values[1], values[2]);
    }

    private static Quaternion ToQuaternion(float[] values)
    {
        if (values == null || values.Length != 4)
        {
            return Quaternion.identity;
        }

        return new Quaternion(values[0], values[1], values[2], values[3]);
    }

    [Serializable]
    private class TrackedFrameDto
    {
        [JsonProperty("camera_translation")]
        public float[] CameraTranslation { get; set; }

        [JsonProperty("camera_rotation")]
        public float[] CameraRotation { get; set; }

        [JsonProperty("classes")]
        public List<TrackedClassDto> Classes { get; set; }
    }

    [Serializable]
    private class TrackedClassDto
    {
        [JsonProperty("class_name")]
        public string ClassName { get; set; }

        [JsonProperty("reconstructed_meshes")]
        public List<ReconstructedMeshDto> ReconstructedMeshes { get; set; }
    }

    [Serializable]
    private class ReconstructedMeshDto
    {
        [JsonProperty("scale")]
        public float[] Scale { get; set; }

        [JsonProperty("rotation")]
        public float[] Rotation { get; set; }

        [JsonProperty("translation")]
        public float[] Translation { get; set; }

        [JsonProperty("chunk_relative_rotation")]
        public float[] ChunkRelativeRotation { get; set; }

        [JsonProperty("chunk_relative_translation")]
        public float[] ChunkRelativeTranslation { get; set; }
    }
}
