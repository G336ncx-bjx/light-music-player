using System;
using System.IO;
using System.Text;

namespace Skylark
{
    /// <summary>
    /// 内置 FLAC 解码器：把 .flac 解成 WAV，让系统播放内核也能放无损格式，
    /// 这样就不需要用户额外安装 ffmpeg。
    /// 只做解码（不支持 32-bit 浮点等罕见情形，遇到会报错并回退到 ffmpeg）。
    /// </summary>
    public static class FlacDecoder
    {
        public static bool Decode(string flacPath, string wavPath, out string error)
        {
            return Decode(flacPath, wavPath, null, out error);
        }

        public static bool Decode(string flacPath, string wavPath, Action<int> progress, out string error)
        {
            error = null;
            try
            {
                using (FileStream input = new FileStream(flacPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] magic = new byte[4];
                    if (input.Read(magic, 0, 4) != 4 || magic[0] != 'f' || magic[1] != 'L' || magic[2] != 'a' || magic[3] != 'C')
                    {
                        error = "不是有效的 FLAC 文件";
                        return false;
                    }

                    int sampleRate = 0;
                    int channels = 0;
                    int bitsPerSample = 0;
                    long totalSamples = 0;
                    int maxBlockSize = 0;

                    // ---- 元数据块 ----
                    bool last = false;
                    while (!last)
                    {
                        int header = input.ReadByte();
                        if (header < 0) throw new InvalidDataException("元数据不完整");
                        last = (header & 0x80) != 0;
                        int type = header & 0x7F;
                        int length = ReadInt24(input);
                        byte[] data = new byte[length];
                        int got = 0;
                        while (got < length)
                        {
                            int n = input.Read(data, got, length - got);
                            if (n <= 0) throw new InvalidDataException("元数据不完整");
                            got += n;
                        }
                        if (type == 0 && length >= 34)
                        {
                            maxBlockSize = (data[2] << 8) | data[3];
                            sampleRate = (data[10] << 12) | (data[11] << 4) | (data[12] >> 4);
                            channels = ((data[12] >> 1) & 0x07) + 1;
                            bitsPerSample = (((data[12] & 0x01) << 4) | (data[13] >> 4)) + 1;
                            totalSamples = ((long)(data[13] & 0x0F) << 32) | ((long)data[14] << 24)
                                | ((long)data[15] << 16) | ((long)data[16] << 8) | data[17];
                        }
                    }
                    if (sampleRate <= 0 || channels <= 0 || bitsPerSample <= 0)
                    {
                        error = "FLAC 头信息不完整";
                        return false;
                    }
                    if (bitsPerSample > 24)
                    {
                        error = "暂不支持 " + bitsPerSample + " bit 的 FLAC";
                        return false;
                    }

                    int outputBits = bitsPerSample <= 8 ? 8 : (bitsPerSample <= 16 ? 16 : 24);
                    int bytesPerSample = outputBits / 8;
                    int shift = outputBits - bitsPerSample;

                    // ---- 写 WAV 头（大小先占位）----
                    long dataStart;
                    // 先写到 .part，全部解完再改名：这样即使中途失败/被杀，
                    // 也不会留下一个「看起来存在但其实是半截」的 WAV 被拿去播放。
                    string tempPath = wavPath + ".part";
                    using (FileStream output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        BinaryWriter writer = new BinaryWriter(output);
                        WriteWavHeader(writer, sampleRate, channels, outputBits, 0);
                        dataStart = output.Position;

                        BitReader reader = new BitReader(input);
                        long written = 0;
                        if (maxBlockSize <= 0 || maxBlockSize > 65535) maxBlockSize = 65535;
                        int[] channelSamples = new int[channels * maxBlockSize];
                        int[] side = new int[channels];
                        int frameCount = 0;

                        while (true)
                        {
                            bool endOfStream;
                            int blockSize;
                            try
                            {
                                blockSize = DecodeFrame(reader, input, ref sampleRate, channels, bitsPerSample,
                                    channelSamples, side, out endOfStream);
                            }
                            catch (IndexOutOfRangeException ex)
                            {
                                throw new InvalidDataException("解码第 " + (frameCount + 1)
                                    + " 帧时数组越界（channels=" + channels + ", bps=" + bitsPerSample
                                    + ", maxBlock=" + maxBlockSize + "）：" + FirstFrame(ex.StackTrace));
                            }
                            if (endOfStream) break;
                            frameCount++;

                            for (int i = 0; i < blockSize; i++)
                            {
                                for (int c = 0; c < channels; c++)
                                {
                                    int value = channelSamples[c * blockSize + i];
                                    WriteSample(writer, value, outputBits, shift);
                                }
                            }
                            written += blockSize;
                            if (progress != null && totalSamples > 0)
                            {
                                int percent = (int)Math.Min(100, written * 100 / totalSamples);
                                progress(percent);
                            }

                            // 每处理一段就把数据刷出去，避免内存里堆着
                            writer.Flush();
                        }

                        long dataBytes = output.Position - dataStart;
                        output.Position = 0;
                        WriteWavHeader(writer, sampleRate, channels, outputBits, dataBytes);
                        writer.Flush();
                    }
                    if (File.Exists(wavPath)) File.Delete(wavPath);
                    File.Move(tempPath, wavPath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void WriteWavHeader(BinaryWriter writer, int sampleRate, int channels,
            int bitsPerSample, long dataBytes)
        {
            int blockAlign = channels * (bitsPerSample / 8);
            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write((int)(36 + dataBytes));
            writer.Write(new char[] { 'W', 'A', 'V', 'E' });
            writer.Write(new char[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);                       // PCM
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * blockAlign);         // byte rate
            writer.Write((short)blockAlign);
            writer.Write((short)bitsPerSample);
            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write((int)dataBytes);
        }

        private static void WriteSample(BinaryWriter writer, int value, int bitsPerSample, int shift)
        {
            int sample = shift > 0 ? value << shift : value;
            if (bitsPerSample == 8) writer.Write((byte)(sample + 128));      // WAV 8bit 是无符号
            else if (bitsPerSample == 16) writer.Write((short)sample);
            else
            {
                writer.Write((byte)(sample & 0xFF));
                writer.Write((byte)((sample >> 8) & 0xFF));
                writer.Write((byte)((sample >> 16) & 0xFF));
            }
        }

        /// <summary>取堆栈里第一行（用于定位越界发生在哪个方法）。</summary>
        private static string FirstFrame(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return "";
            string[] lines = stackTrace.Split('\n');
            return lines.Length > 0 ? lines[0].Trim() : "";
        }

        private static int ReadInt24(Stream stream)
        {
            int b0 = stream.ReadByte();
            int b1 = stream.ReadByte();
            int b2 = stream.ReadByte();
            if (b0 < 0 || b1 < 0 || b2 < 0) throw new InvalidDataException("文件不完整");
            return (b0 << 16) | (b1 << 8) | b2;
        }

        /// <summary>解一帧，返回块大小；channelSamples 里按「通道优先」存放该帧样本。</summary>
        private static int DecodeFrame(BitReader reader, Stream input, ref int sampleRate,
            int channels, int bitsPerSample, int[] channelSamples, int[] side, out bool endOfStream)
        {
            endOfStream = false;
            // 找同步字 0b11111111111110
            int reserved;
            int blocking;
            if (!reader.SyncToFrame(out reserved, out blocking)) { endOfStream = true; return 0; }
            int blockSizeCode = reader.ReadBits(4);
            int sampleRateCode = reader.ReadBits(4);
            int channelCode = reader.ReadBits(4);
            int sampleSizeCode = reader.ReadBits(3);
            reader.ReadBit();                       // reserved

            if (blocking == 0) reader.ReadUtf8Number();
            else reader.ReadUtf8Number();

            int blockSize;
            if (blockSizeCode == 0) blockSize = 0;
            else if (blockSizeCode == 1) blockSize = 192;
            else if (blockSizeCode >= 2 && blockSizeCode <= 5) blockSize = 576 << (blockSizeCode - 2);
            else if (blockSizeCode == 6) blockSize = reader.ReadBits(8) + 1;
            else if (blockSizeCode == 7) blockSize = reader.ReadBits(16) + 1;
            else blockSize = 256 << (blockSizeCode - 8);
            if (blockSize <= 0) throw new InvalidDataException("块大小无效");

            if (sampleRateCode == 12) reader.ReadBits(8);
            else if (sampleRateCode == 13) reader.ReadBits(16);
            else if (sampleRateCode == 14) reader.ReadBits(16);

            reader.ReadBits(8);                     // 头部 CRC-8，不校验

            int frameChannels = channelCode < 8 ? channelCode + 1 : 2;
            if (frameChannels > channels) frameChannels = channels;

            int bps = bitsPerSample;
            if (sampleSizeCode == 1) bps = 8;
            else if (sampleSizeCode == 2) bps = 12;
            else if (sampleSizeCode == 4) bps = 16;
            else if (sampleSizeCode == 5) bps = 20;
            else if (sampleSizeCode == 6) bps = 24;

            if (channelSamples.Length < blockSize * frameChannels)
            {
                throw new InvalidDataException("缓冲区不足");
            }

            for (int c = 0; c < frameChannels; c++)
            {
                int channelBits = bps;
                // 侧声道需要多一位
                if (channelCode == 8 && c == 1) channelBits = bps + 1;
                else if (channelCode == 9 && c == 0) channelBits = bps + 1;
                else if (channelCode == 10 && c == 1) channelBits = bps + 1;
                DecodeSubframe(reader, blockSize, channelBits, channelSamples, c * blockSize);
            }

            reader.AlignToByte();
            reader.ReadBits(16);                    // 帧 CRC-16

            // 声道去相关
            if (channelCode == 8)           // left / side
            {
                for (int i = 0; i < blockSize; i++)
                {
                    int left = channelSamples[i];
                    int diff = channelSamples[blockSize + i];
                    channelSamples[blockSize + i] = left - diff;
                }
            }
            else if (channelCode == 9)      // side / right
            {
                for (int i = 0; i < blockSize; i++)
                {
                    int diff = channelSamples[i];
                    int right = channelSamples[blockSize + i];
                    channelSamples[i] = diff + right;
                }
            }
            else if (channelCode == 10)     // mid / side
            {
                for (int i = 0; i < blockSize; i++)
                {
                    int mid = channelSamples[i];
                    int diff = channelSamples[blockSize + i];
                    mid = (mid << 1) | (diff & 1);
                    channelSamples[i] = (mid + diff) >> 1;
                    channelSamples[blockSize + i] = (mid - diff) >> 1;
                }
            }
            return blockSize;
        }

        private static void DecodeSubframe(BitReader reader, int blockSize, int bitsPerSample,
            int[] output, int offset)
        {
            if (reader.ReadBit() != 0) throw new InvalidDataException("子帧填充位错误");
            int type = reader.ReadBits(6);
            int wasted = 0;
            if (reader.ReadBit() == 1)
            {
                wasted = reader.ReadUnary() + 1;
                bitsPerSample -= wasted;
                if (bitsPerSample <= 0) throw new InvalidDataException("子帧位深无效");
            }

            if (type == 0)                  // CONSTANT
            {
                int value = (int)reader.ReadSignedBits(bitsPerSample);
                for (int i = 0; i < blockSize; i++) output[offset + i] = value;
            }
            else if (type == 1)             // VERBATIM
            {
                for (int i = 0; i < blockSize; i++)
                    output[offset + i] = (int)reader.ReadSignedBits(bitsPerSample);
            }
            else if (type >= 8 && type <= 12)   // FIXED
            {
                int order = type - 8;
                DecodeFixed(reader, blockSize, bitsPerSample, order, output, offset);
            }
            else if (type >= 32)                // LPC
            {
                int order = type - 31;
                DecodeLpc(reader, blockSize, bitsPerSample, order, output, offset);
            }
            else throw new InvalidDataException("不支持的子帧类型 " + type);

            if (wasted > 0)
            {
                for (int i = 0; i < blockSize; i++) output[offset + i] <<= wasted;
            }
        }

        private static void DecodeFixed(BitReader reader, int blockSize, int bitsPerSample,
            int order, int[] output, int offset)
        {
            for (int i = 0; i < order; i++)
                output[offset + i] = (int)reader.ReadSignedBits(bitsPerSample);
            DecodeResidual(reader, blockSize, order, output, offset);

            for (int i = order; i < blockSize; i++)
            {
                int value;
                switch (order)
                {
                    case 0: value = 0; break;
                    case 1: value = output[offset + i - 1]; break;
                    case 2:
                        value = 2 * output[offset + i - 1] - output[offset + i - 2];
                        break;
                    case 3:
                        value = 3 * output[offset + i - 1] - 3 * output[offset + i - 2]
                            + output[offset + i - 3];
                        break;
                    default:
                        value = 4 * output[offset + i - 1] - 6 * output[offset + i - 2]
                            + 4 * output[offset + i - 3] - output[offset + i - 4];
                        break;
                }
                output[offset + i] += value;
            }
        }

        private static void DecodeLpc(BitReader reader, int blockSize, int bitsPerSample,
            int order, int[] output, int offset)
        {
            for (int i = 0; i < order; i++)
                output[offset + i] = (int)reader.ReadSignedBits(bitsPerSample);

            int precision = reader.ReadBits(4);
            if (precision == 15) throw new InvalidDataException("LPC 精度无效");
            precision += 1;
            int shift = (int)reader.ReadSignedBits(5);

            int[] coefficients = new int[order];
            for (int i = 0; i < order; i++)
                coefficients[i] = (int)reader.ReadSignedBits(precision);

            DecodeResidual(reader, blockSize, order, output, offset);

            for (int i = order; i < blockSize; i++)
            {
                long sum = 0;
                for (int j = 0; j < order; j++)
                    sum += (long)coefficients[j] * output[offset + i - 1 - j];
                int value = (int)(sum >> shift);
                output[offset + i] += value;
            }
        }

        private static void DecodeResidual(BitReader reader, int blockSize, int order,
            int[] output, int offset)
        {
            DecodeResidualCore(reader, blockSize, order, output, offset);
        }

        private static void CheckBounds(int index, int offset, int blockSize, int partitionOrder,
            int order, int partition, int count)
        {
            if (index < offset || index >= offset + blockSize)
            {
                throw new InvalidDataException("残差越界：index=" + index + " offset=" + offset
                    + " blockSize=" + blockSize + " partitionOrder=" + partitionOrder
                    + " order=" + order + " partition=" + partition + " count=" + count);
            }
        }

        private static void DecodeResidualCore(BitReader reader, int blockSize, int order,
            int[] output, int offset)
        {
            int method = reader.ReadBits(2);
            if (method > 1) throw new InvalidDataException("残差编码方式无效");
            int paramBits = method == 0 ? 4 : 5;
            int escape = method == 0 ? 15 : 31;
            int partitionOrder = reader.ReadBits(4);
            int partitions = 1 << partitionOrder;

            for (int p = 0; p < partitions; p++)
            {
                int count = blockSize >> partitionOrder;
                if (p == 0) count -= order;
                int parameter = reader.ReadBits(paramBits);
                if (parameter == escape)
                {
                    int rawBits = reader.ReadBits(5);
                    for (int i = 0; i < count; i++)
                    {
                        int index = offset + (blockSize >> partitionOrder) * p + (p == 0 ? order : 0) + i;
                        CheckBounds(index, offset, blockSize, partitionOrder, order, p, count);
                        output[index] = (int)reader.ReadSignedBits(rawBits);
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        int quotient = reader.ReadUnary();
                        int remainder = parameter > 0 ? reader.ReadBits(parameter) : 0;
                        int value = (quotient << parameter) | remainder;
                        value = (value >> 1) ^ -(value & 1);   // zigzag 还原
                        int index = offset + (blockSize >> partitionOrder) * p + (p == 0 ? order : 0) + i;
                        CheckBounds(index, offset, blockSize, partitionOrder, order, p, count);
                        output[index] = value;
                    }
                }
            }
            // 上面写进去的是残差，叠加到预测值由调用方完成
            // （这里已经把残差直接写入 output，预测循环会在其后叠加）
            return;
        }

        /// <summary>按位（高位在前）读取 FLAC 数据流。</summary>
        private class BitReader
        {
            private readonly Stream stream;
            private int current;
            private int bitsLeft;

            public BitReader(Stream stream)
            {
                this.stream = stream;
            }

            public int ReadBit()
            {
                if (bitsLeft == 0)
                {
                    int b = stream.ReadByte();
                    if (b < 0) throw new EndOfStreamException();
                    current = b;
                    bitsLeft = 8;
                }
                bitsLeft--;
                return (current >> bitsLeft) & 1;
            }

            public int ReadBits(int count)
            {
                int value = 0;
                for (int i = 0; i < count; i++) value = (value << 1) | ReadBit();
                return value;
            }

            public long ReadSignedBits(int count)
            {
                if (count == 0) return 0;
                long value = ReadBits(count);
                long sign = 1L << (count - 1);
                return (value & sign) != 0 ? value - (sign << 1) : value;
            }

            /// <summary>读取一元编码（前导 0 的个数）。</summary>
            public int ReadUnary()
            {
                int count = 0;
                while (ReadBit() == 0)
                {
                    count++;
                    if (count > 1000000) throw new InvalidDataException("一元编码异常");
                }
                return count;
            }

            public void AlignToByte()
            {
                bitsLeft = 0;
            }

            public long ReadUtf8Number()
            {
                int first = ReadBits(8);
                if ((first & 0x80) == 0) return first;
                int extra = 0;
                if ((first & 0xE0) == 0xC0) extra = 1;
                else if ((first & 0xF0) == 0xE0) extra = 2;
                else if ((first & 0xF8) == 0xF0) extra = 3;
                else if ((first & 0xFC) == 0xF8) extra = 4;
                else if ((first & 0xFE) == 0xFC) extra = 5;
                long value = first & (0x7F >> extra);
                for (int i = 0; i < extra; i++) value = (value << 6) | (long)(uint)(ReadBits(8) & 0x3F);
                return value;
            }

            /// <summary>
            /// 寻找 14 位同步字（0xFF + 高 6 位为 111110 的字节），
            /// 并顺便取出紧随其后的 reserved 与 blocking strategy 两个位。
            /// </summary>
            public bool SyncToFrame(out int reserved, out int blocking)
            {
                reserved = 0;
                blocking = 0;
                int previous = -1;
                while (true)
                {
                    int b;
                    try
                    {
                        b = ReadBits(8);
                    }
                    catch (EndOfStreamException)
                    {
                        return false;
                    }
                    if (previous == 0xFF && (b & 0xFE) == 0xF8)
                    {
                        blocking = b & 0x01;
                        reserved = (b >> 1) & 0x01;
                        return true;
                    }
                    previous = b;
                }
            }
        }
    }
}
