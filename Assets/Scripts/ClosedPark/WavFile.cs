using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 单声道 PCM16 WAV 的读写。
///
/// Unity 没有把 AudioClip 写成文件的 API，而园区语音消息要落到眼镜本地、之后还能回放，
/// 所以自己写这一小段。只处理本类自己写出的格式（单声道 16 位），不是通用解码器——
/// 读到别的格式直接报错，不去猜。
/// </summary>
public static class WavFile
{
    private const int HeaderSize = 44;

    public static void Write(string path, float[] samples, int frequency)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));

        int dataSize = samples.Length * 2;
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(HeaderSize - 8 + dataSize);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);                 // fmt 块长度
            writer.Write((short)1);           // PCM
            writer.Write((short)1);           // 单声道
            writer.Write(frequency);
            writer.Write(frequency * 2);      // 字节率
            writer.Write((short)2);           // 块对齐
            writer.Write((short)16);          // 位深
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);
            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                writer.Write((short)Mathf.RoundToInt(clamped * short.MaxValue));
            }
        }
    }

    public static float[] Read(string path, out int frequency)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < HeaderSize || bytes[0] != 'R' || bytes[8] != 'W')
            throw new InvalidDataException("不是 WAV 文件");

        short channels = BitConverter.ToInt16(bytes, 22);
        short bits = BitConverter.ToInt16(bytes, 34);
        if (channels != 1 || bits != 16)
            throw new InvalidDataException($"只支持单声道 16 位 WAV，实际 {channels} 声道 {bits} 位");

        frequency = BitConverter.ToInt32(bytes, 24);
        int dataSize = Mathf.Min(BitConverter.ToInt32(bytes, 40), bytes.Length - HeaderSize);
        var samples = new float[dataSize / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(bytes, HeaderSize + i * 2) / (float)short.MaxValue;
        return samples;
    }
}
