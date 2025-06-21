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
            byte[] data = File.ReadAllBytes(fileName);
            bool isRaw = Encoding.ASCII.GetString(data, 0, 3) == "YM5" || Encoding.ASCII.GetString(data, 0, 3) == "YM6" || Encoding.ASCII.GetString(data, 0, 3).StartsWith("YM3") || Encoding.ASCII.GetString(data, 0, 3).StartsWith("YM4");
            if (!isRaw)
            {
                var lha = new LhaFile(fileName, Encoding.UTF8);
                var entry = lha.GetEntry(0);
                data = lha.GetEntryBytes(entry);
            }

            using var br = new BinaryReader(new MemoryStream(data), Encoding.ASCII, false);
            _type = new string(br.ReadChars(4)); // "YM3!", "YM3b", "YM4!", "YM5!", "YM6!"

            if (_type == "YM3b")
            {
                long payloadSize = data.Length - 8;
                _frameCount = (int)(payloadSize / 14);
                byte[] raw = br.ReadBytes(_frameCount * 14);
                _frameLoop = (int)br.ReadUInt32(); // little-endian

                _bytes = new byte[_frameCount * 16];
                for (int reg = 0; reg < 14; reg++)
                {
                    int baseIndex = reg * _frameCount;
                    for (int f = 0; f < _frameCount; f++)
                    {
                        _bytes[f * 16 + reg] = raw[baseIndex + f];
                    }
                }
            }
            else
            {
                // Standard YM5/YM6 header parsing
                string check = new string(br.ReadChars(8));
                if (check != "LeOnArD!") throw new InvalidDataException("Not a YM5/6 file");
                _frameCount = (int)SwapByteOrder(br.ReadUInt32());
                _songAttributes = SwapByteOrder(br.ReadUInt32());
                _digidrumsSamples = SwapByteOrder(br.ReadUInt16());
                _ymFrequency = SwapByteOrder(br.ReadUInt32());
                _frameRate = SwapByteOrder(br.ReadUInt16());
                _frameLoop = (int)SwapByteOrder(br.ReadUInt32());
                br.ReadUInt16(); // extra data length

                _title = ReadNullTerminationString(br, Encoding.ASCII);
                _artist = ReadNullTerminationString(br, Encoding.ASCII);
                _comments = ReadNullTerminationString(br, Encoding.ASCII);

                byte[] raw = br.ReadBytes(16 * _frameCount);
                if ((_songAttributes & 1) != 0)
                    _bytes = DeInterleave(raw, _frameCount);
                else
                    _bytes = raw;
            }

            if (_frameRate == 0)
                _frameRate = 50;

            if (_ymFrequency == 0)
                _ymFrequency = 2000000; // default to 2 MHz if not specified

            _totalTime = TimeSpan.FromSeconds((double)_frameCount / _frameRate);
        }

        private byte[] DeInterleave(byte[] raw, int frames)
        {
            var outp = new byte[raw.Length];
            for (int r = 0; r < 16; r++)
                for (int f = 0; f < frames; f++)
                    outp[f * 16 + r] = raw[r * frames + f];
            return outp;
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
