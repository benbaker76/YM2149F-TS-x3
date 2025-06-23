// benbaker76 (https://github.com/benbaker76)

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
        public enum EffectType
        {
            None = 0,
            TimerSynth = 1,
            DigiDrum = 2
        }

        private readonly bool _isYM6;

        private string _fileName;
        private string _type;
        private uint _songAttributes;
        private ushort _digidrumsSamples;
        private uint _ymFrequency;
        private string _title, _artist, _comments;
        private int _frameCount, _frameLoop, _frameRate;
        private TimeSpan _totalTime;
        private byte[] _bytes;

        public YMParser(string fileName)
        {
            _fileName = fileName;
            byte[] data = File.ReadAllBytes(fileName);
            bool isRaw = Encoding.ASCII.GetString(data, 0, 4).StartsWith("YM");
            if (!isRaw)
            {
                var lha = new LhaFile(fileName, Encoding.UTF8);
                data = lha.GetEntryBytes(lha.GetEntry(0));
            }

            using var br = new BinaryReader(new MemoryStream(data), Encoding.ASCII, false);
            _type = new string(br.ReadChars(4));

            if (_type == "YM3b")
            {
                int primitiveFrames = (data.Length - 8) / 14;
                _frameCount = primitiveFrames;
                byte[] raw = br.ReadBytes(_frameCount * 14);
                _frameLoop = (int)br.ReadUInt32();

                _bytes = new byte[_frameCount * 16];
                for (int reg = 0; reg < 14; reg++)
                    for (int f = 0; f < _frameCount; f++)
                        _bytes[f * 16 + reg] = raw[reg * _frameCount + f];

                _digidrumsSamples = 0;
                _ymFrequency = 0;
                _frameRate = 0;
            }
            else
            {
                string check = new string(br.ReadChars(8));
                if (check != "LeOnArD!")
                    throw new InvalidDataException("Not a valid YM5/YM6 file");

                _frameCount = (int)Swap(br.ReadUInt32());
                _songAttributes = Swap(br.ReadUInt32());
                _digidrumsSamples = Swap(br.ReadUInt16());
                _ymFrequency = Swap(br.ReadUInt32());
                _frameRate = Swap(br.ReadUInt16());
                _frameLoop = (int)Swap(br.ReadUInt32());

                ushort extra = br.ReadUInt16();
                _title = ReadNullString(br);
                _artist = ReadNullString(br);
                _comments = ReadNullString(br);

                if (_digidrumsSamples > 0)
                {
                    for (int i = 0; i < _digidrumsSamples; i++)
                    {
                        uint size = Swap(br.ReadUInt32());
                        br.BaseStream.Seek(size, SeekOrigin.Current);
                    }
                }

                if (extra > 0)
                    br.BaseStream.Seek(extra, SeekOrigin.Current);

                _isYM6 = _type == "YM6!";

                byte[] raw = br.ReadBytes(_frameCount * 16);
                _bytes = ((_songAttributes & 1) != 0) ? DeInterleave(raw, _frameCount) : raw;
            }

            if (_frameRate == 0) _frameRate = 50;
            if (_ymFrequency == 0) _ymFrequency = 2000000;

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

        private string ReadNullString(BinaryReader br)
        {
            var sb = new StringBuilder();
            byte b;
            while ((b = br.ReadByte()) != 0)
                sb.Append((char)b);
            return sb.ToString();
        }

        public IEnumerable<EffectInfo> GetEffects(int frame)
        {
            if (!_isYM6) yield break;

            // slot‑1  (TS)   : r1 / r6 / r14
            // slot‑2  (DD)   : r3 / r8 / r15
            EffectInfo?[] slots =
            {
                Decode(frame, 1, 6, 14),
                Decode(frame, 3, 8, 15)
            };

            foreach (var fx in slots)
                if (fx.HasValue) yield return fx.Value;
        }

        private EffectInfo? Decode(int frame, int flagR, int timerR, int countR)
        {
            int baseIdx = frame * 16;

            byte flag = _bytes[baseIdx + flagR];

            /* ----- voice (01=A, 10=B, 11=C) ----- */
            int vBits  = (flag >> 4) & 0x03;       // r?.5‑4
            if (vBits == 0) return null;           // no effect in this slot
            int voice  = vBits - 1;                // 0=A,1=B,2=C

            /* ----- which effect?  ----- */
            EffectType type = flagR switch
            {
                1 => EffectType.TimerSynth,        // slot‑1 → TS
                3 => EffectType.DigiDrum,          // slot‑2 → DD
                _ => EffectType.None               // should never happen
            };

            /* ----- extra flags ----- */
            bool restart = (type == EffectType.TimerSynth) && ((flag & 0x40) != 0);

            /* ----- timer values ----- */
            int divisor = (_bytes[baseIdx + timerR] >> 5) & 0x07;   // TP (3 bits)
            int count   =  _bytes[baseIdx + countR];                // TC (8 bits)

            return new EffectInfo(type, voice, divisor, count, restart);
        }

        public struct EffectInfo
        {
            public EffectType Type;
            public int Voice;
            public int TimerDivisor;
            public int TimerCount;
            public bool Restart;

            public EffectInfo(EffectType type, int voice, int timerDivisor, int timerCount, bool restart)
            {
                Type = type;
                Voice = voice;
                TimerDivisor = timerDivisor;
                TimerCount = timerCount;
                Restart = restart;
            }

            public override string ToString() =>
                $"{Type.ToString()} (Voice {Voice}, Div {TimerDivisor}, Count {TimerCount} Restart {Restart})";
        }

        private static uint Swap(uint v) =>
            (v >> 24) | ((v >> 8) & 0x0000FF00) | ((v << 8) & 0x00FF0000) | (v << 24);

        private static ushort Swap(ushort v) => (ushort)(((v >> 8) & 0x00FF) | ((v << 8) & 0xFF00));

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
