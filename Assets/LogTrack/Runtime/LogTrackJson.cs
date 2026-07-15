using System;
using System.Linq;
#if !CONSOLE_DEMO
using UnityEngine;
#else
using System.Text.Json;
#endif

/// <summary>
/// Unity / Console 兼容的 JSON 读写。
/// </summary>
internal static class LogTrackJson
{
    [Serializable]
    private class LogTrackFrameDto
    {
        public int frameIndex;
        public int[] items;
        public int[] args;
        public int[] depths;
        public int[] phases;
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
        public string className = string.Empty;
        public string funcName = string.Empty;
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
        return ToJson(dto);
    }

    public static LogTrackFile DeserializeLogTrackFile(string json)
    {
        var dto = FromJson<LogTrackFileDto>(json);
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
                dbgStr = item.dbgStr ?? string.Empty,
                className = item.className ?? string.Empty,
                funcName = item.funcName ?? string.Empty
            }).ToArray() ?? Array.Empty<LogTrackPdbItemDto>()
        };
        return ToJson(dto);
    }

    public static LogTrackPdbFile DeserializeLogTrackPdbFile(string json)
    {
        var dto = FromJson<LogTrackPdbFileDto>(json);
        if (dto == null)
        {
            return null;
        }

        var file = new LogTrackPdbFile();
        if (dto.items != null)
        {
            foreach (var itemDto in dto.items)
            {
                var item = new LogTrackPdbItem
                {
                    hash = itemDto.hash,
                    argCount = itemDto.argCount,
                    file = itemDto.file ?? string.Empty,
                    line = itemDto.line,
                    dbgStr = itemDto.dbgStr ?? string.Empty,
                    className = itemDto.className ?? string.Empty,
                    funcName = itemDto.funcName ?? string.Empty
                };
                file.items.Add(item);
                file.RegisterItem(item);
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
            args = frame.args?.ToArray() ?? Array.Empty<int>(),
            depths = frame.depths?.Select(d => (int)d).ToArray() ?? Array.Empty<int>(),
            phases = frame.phases?.Select(p => (int)p).ToArray() ?? Array.Empty<int>()
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

        if (dto.depths != null)
        {
            foreach (var depth in dto.depths)
            {
                frame.depths.Add((byte)depth);
            }
        }

        if (dto.phases != null)
        {
            foreach (var phase in dto.phases)
            {
                frame.phases.Add((byte)phase);
            }
        }

        return frame;
    }

#if CONSOLE_DEMO
    private static readonly JsonSerializerOptions ConsoleJsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
        IncludeFields = true,
    };
#endif

    private static string ToJson<T>(T dto)
    {
#if CONSOLE_DEMO
        return JsonSerializer.Serialize(dto, ConsoleJsonOptions);
#else
        return JsonUtility.ToJson(dto);
#endif
    }

    private static T FromJson<T>(string json) where T : class
    {
#if CONSOLE_DEMO
        return JsonSerializer.Deserialize<T>(json, ConsoleJsonOptions);
#else
        return JsonUtility.FromJson<T>(json);
#endif
    }
}
