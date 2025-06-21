// Original code by Mr Megahertz
// Url: https://pastebin.com/54nA1EaH
// Updates by benbaker76 (https://github.com/benbaker76)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LHADecompressor;

namespace YMPlayer
{
    public class YMParser
    {
        // http://leonard.oxg.free.fr/ymformat.html

        private readonly bool _isYM6;

        private string _fileName = null;
        private string _type = null;
        private uint _songAttributes = 0;
        private ushort _digidrumsSamples = 0;
        private uint _ymFrequency = 0;

        private string _title = null;
        private string _artist = null;
        private string _comments = null;

        private int _frameCount = 0;
        private int _frameLoop = 0;
        private int _frameRate = 0;
        private TimeSpan _totalTime;
        private byte[] _bytes = null;

        public YMParser(string fileName)
        {
            _fileName = Path.GetFileName(fileName);

            byte[] bytes = File.ReadAllBytes(fileName);

            // Detect compression vs raw
            bool isRaw = Encoding.ASCII.GetString(bytes, 0, 2) == "YM";
            if (!isRaw)
            {
                var lha = new LhaFile(fileName, Encoding.UTF8);
                var entry = lha.GetEntry(0);
                bytes = lha.GetEntryBytes(entry);
            }

            using (var ms = new MemoryStream(bytes))
            using (var br = new BinaryReader(ms))
            {
                var enc = Encoding.ASCII;
                _type = enc.GetString(br.ReadBytes(4)); // e.g., "YM5!" or "YM6!"
                _isYM6 = _type == "YM6!";
                var check = enc.GetString(br.ReadBytes(8));
                if (check != "LeOnArD!") return;

                _frameCount = (int)SwapByteOrder(br.ReadUInt32());
                _songAttributes = SwapByteOrder(br.ReadUInt32());
                _digidrumsSamples = SwapByteOrder(br.ReadUInt16());
                _ymFrequency = SwapByteOrder(br.ReadUInt32());
                _frameRate = SwapByteOrder(br.ReadUInt16());
                _frameLoop = (int)SwapByteOrder(br.ReadUInt32());

                br.ReadUInt16(); // unused / extra data length

                _title = ReadNullTerminationString(br, enc);
                _artist = ReadNullTerminationString(br, enc);
                _comments = ReadNullTerminationString(br, enc);

                _bytes = ReadAllFrames(br, _frameCount, _songAttributes);
            }

            _totalTime = TimeSpan.FromSeconds((double)_frameCount / _frameRate);
        }

        public IEnumerable<EffectInfo> GetEffects(int frame)
        {
            if (!_isYM6) yield break;

            byte r1 = _bytes[frame * 16 + 1];
            byte r3 = _bytes[frame * 16 + 3];
            byte r6 = _bytes[frame * 16 + 6];
            byte r8 = _bytes[frame * 16 + 8];
            byte r14 = _bytes[frame * 16 + 14];
            byte r15 = _bytes[frame * 16 + 15];

            var e1 = DecodeEffectFrame(r1, r6, r14);
            if (e1 != null) yield return e1.Value;

            var e2 = DecodeEffectFrame(r3, r8, r15);
            if (e2 != null) yield return e2.Value;
        }

        private EffectInfo? DecodeEffectFrame(byte flagReg, byte timer1, byte timer2)
        {
            int type = (flagReg >> 6) & 0x3;
            if (type == 0) return null;

            int voice = (flagReg >> 4) & 0x3;
            int timer = ((flagReg & 0xF) << 8) | timer1;
            int volume = timer2; // depending on effect type

            return new EffectInfo { Voice = voice, Type = type, Timer = timer, Volume = volume };
        }

        public struct EffectInfo
        {
            public int Voice, Type, Timer, Volume;
            public override string ToString() =>
                $"Voice {Voice}, Type {Type}, Timer {Timer}, Volume {Volume}";
        }

        private byte[] ReadAllFrames(BinaryReader reader, int frames, uint songAttributes)
        {
            // Total bytes for 16 registers × frame count
            int totalBytes = 16 * frames;
            byte[] raw = reader.ReadBytes(totalBytes);

            // Bit 0 of SongAttributes:
            // 1 = interleaved (YM3/YM5-style), 0 = non-interleaved (frame-major) :contentReference[oaicite:1]{index=1}
            bool interleaved = (songAttributes & 1) != 0;

            if (!interleaved)
            {
                // Data is already in [frame0 regs0–15][frame1 regs0–15]… format
                return raw;
            }

            // De-interleave into frame-major format
            byte[] output = new byte[totalBytes];
            int framesPerReg = frames;

            for (int reg = 0; reg < 16; reg++)
            {
                for (int f = 0; f < framesPerReg; f++)
                {
                    int srcIndex = reg * framesPerReg + f;
                    int destIndex = f * 16 + reg;
                    output[destIndex] = raw[srcIndex];
                }
            }

            return output;
        }

        private string ReadNullTerminationString(BinaryReader reader, Encoding encoding)
        {
            byte[] temp = new byte[1];
            StringBuilder stringBuilder = new StringBuilder();
            while (true)
            {
                temp[0] = reader.ReadByte();

                if (temp[0] == 0x00)
                {
                    return stringBuilder.ToString();
                }
                else
                {
                    stringBuilder.Append(encoding.GetString(temp));
                }
            }
        }

        private uint SwapByteOrder(uint valueToSwap)
        {
            uint uvalue = valueToSwap;
            uint swapped =
                ((0x000000FF) & (uvalue >> 24)
                | (0x0000FF00) & (uvalue >> 8)
                | (0x00FF0000) & (uvalue << 8)
                | (0xFF000000) & (uvalue << 24)
                );
            return swapped;
        }

        private ushort SwapByteOrder(ushort valueToSwap)
        {
            var uvalue = valueToSwap;
            var swapped =
                ((0x00FF) & (uvalue >> 8)
                | (0xFF00) & (uvalue << 8)
                );
            return (ushort)swapped;
        }

        public bool IsYM6 => _isYM6;
        public string FileName { get { return _fileName; } }
        public string Type { get { return _type; } }
        public uint SongAttributes { get { return _songAttributes; } }
        public ushort DigidrumsSamples { get { return _digidrumsSamples; } }
        public uint YmFrequency { get { return _ymFrequency; } }

        public int FrameCount { get { return _frameCount; } }
        public int FrameRate { get { return _frameRate; } }
        public int FrameLoop { get { return _frameLoop; } }

        public TimeSpan TotalTime { get { return _totalTime; } }

        public string Title { get { return _title; } }
        public string Artist { get { return _artist; } }
        public string Comments { get { return _comments; } }

        public byte[] Bytes { get { return _bytes; } }
    }
}
