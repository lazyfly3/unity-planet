using System;
using System.Linq;
using UnityEngine;

/// <summary>
/// Unity 兼容的 JSON 读写。Unity 2022 内置的 System.Text.Json 中 JsonSerializer 不可访问。
/// </summary>
internal static class LogTrackJson
{
    [Serializable]
    private class LogTrackFrameDto
    {
        public int frameIndex;
        public int[] items;
        public int[] args;
    }

    [Serializable]
    private class LogTrackFileDto
    {
        public int errorFrameIndex;
        public string saveDateTime = string.Empty;
        public LogTrackFrameDto[] frames;
    }

    [Serializable]
    private class LogTrackPdbItemDto
    {
        public int hash;
        public int argCount;
        public string file = string.Empty;
        public int line;
        public string dbgStr = string.Empty;
    }

    [Serializable]
    private class LogTrackPdbFileDto
    {
        public LogTrackPdbItemDto[] items;
    }

    public static string Serialize(LogTrackFile file)
    {
        var dto = new LogTrackFileDto
        {
            errorFrameIndex = file.errorFrameIndex,
            saveDateTime = file.saveDateTime ?? string.Empty,
            frames = file.frames?.Select(ToDto).ToArray() ?? Array.Empty<LogTrackFrameDto>()
        };
        return JsonUtility.ToJson(dto);
    }

    public static LogTrackFile DeserializeLogTrackFile(string json)
    {
        var dto = JsonUtility.FromJson<LogTrackFileDto>(json);
        if (dto == null)
        {
            return null;
        }

        var file = new LogTrackFile
        {
            errorFrameIndex = dto.errorFrameIndex,
            saveDateTime = dto.saveDateTime ?? string.Empty
        };

        if (dto.frames != null)
        {
            foreach (var frameDto in dto.frames)
            {
                file.frames.Add(FromDto(frameDto));
            }
        }

        return file;
    }

    public static string Serialize(LogTrackPdbFile file)
    {
        file.Flush();
        var dto = new LogTrackPdbFileDto
        {
            items = file.items?.Select(item => new LogTrackPdbItemDto
            {
                hash = item.hash,
                argCount = item.argCount,
                file = item.file ?? string.Empty,
                line = item.line,
                dbgStr = item.dbgStr ?? string.Empty
            }).ToArray() ?? Array.Empty<LogTrackPdbItemDto>()
        };
        return JsonUtility.ToJson(dto);
    }

    public static LogTrackPdbFile DeserializeLogTrackPdbFile(string json)
    {
        var dto = JsonUtility.FromJson<LogTrackPdbFileDto>(json);
        if (dto == null)
        {
            return null;
        }

        var file = new LogTrackPdbFile();
        if (dto.items != null)
        {
            foreach (var itemDto in dto.items)
            {
                file.items.Add(new LogTrackPdbItem
                {
                    hash = itemDto.hash,
                    argCount = itemDto.argCount,
                    file = itemDto.file ?? string.Empty,
                    line = itemDto.line,
                    dbgStr = itemDto.dbgStr ?? string.Empty
                });
            }
        }

        return file;
    }

    private static LogTrackFrameDto ToDto(LogTrackFrame frame)
    {
        return new LogTrackFrameDto
        {
            frameIndex = frame.frameIndex,
            items = frame.items?.Select(item => (int)item).ToArray() ?? Array.Empty<int>(),
            args = frame.args?.ToArray() ?? Array.Empty<int>()
        };
    }

    private static LogTrackFrame FromDto(LogTrackFrameDto dto)
    {
        var frame = new LogTrackFrame
        {
            frameIndex = dto.frameIndex
        };

        if (dto.items != null)
        {
            foreach (var item in dto.items)
            {
                frame.items.Add((ushort)item);
            }
        }

        if (dto.args != null)
        {
            frame.args.AddRange(dto.args);
        }

        return frame;
    }
}
