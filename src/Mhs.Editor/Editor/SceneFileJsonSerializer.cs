using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Mhs.Editor.Editor;

public sealed class SceneFileData
{
    public int FormatVersion { get; init; } = 1;
    public int ActiveFloor { get; init; }
    public int ActiveLayer { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RendererMode { get; init; }

    public List<SceneFileObjectData> Objects { get; init; } = [];
}

public sealed class SceneFileObjectData
{
    public string PartId { get; init; } = string.Empty;
    public VoxelCoord Position { get; init; }
    public int RotationZDegrees { get; init; }
    public float MaterialUnitsPerSecond { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MaterialId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VoxelSize? SizeOverride { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VoxelCoord? RouteStartCell { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VoxelCoord? RouteEndCell { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RouteFlowReversed { get; init; }
}

public static class SceneFileJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static async Task<SceneFileData> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var data = await JsonSerializer.DeserializeAsync<SceneFileData>(stream, Options, cancellationToken);
        if (data is null)
        {
            throw new InvalidDataException("Scene file was empty.");
        }

        return data;
    }

    public static Task SaveAsync(Stream stream, SceneFileData data, CancellationToken cancellationToken = default)
        => JsonSerializer.SerializeAsync(stream, data, Options, cancellationToken);
}
