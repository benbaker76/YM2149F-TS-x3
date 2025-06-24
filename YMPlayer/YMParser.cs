// benbaker76 (https://github.com/benbaker76)

using LHADecompressor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static YMPlayer.YMParser;

namespace YMPlayer
{
    public class YMParser
    {
        public enum EffectType
        {
            None = 0,
            SIDVoice = 1,
            DigiDrum = 2,
            SinusSID = 3,
            SyncBuzzer = 4,
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

        private readonly List<DigiDrumSample> _digiDrumList = new();

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

                byte[] raw;

                if (_digidrumsSamples > 0)
                {
                    bool signed = (_songAttributes & (1U << 1)) != 0;   // b1 – signed
                    bool st4Bits = (_songAttributes & (1U << 2)) != 0;   // b2 – 4‑bit ST

                    for (int i = 0; i < _digidrumsSamples; ++i)
                    {
                        uint size = Swap(br.ReadUInt32());               // sample length

                        raw = br.ReadBytes((int)size);
                        if (raw.Length != size)
                            throw new EndOfStreamException(
                                $"DigiDrum #{i}: truncated inside file.");

                        // if sample is already in 4‑bit ST format, expand to 8‑bit
                        if (st4Bits)
                        {
                            var tmp = new byte[size * 2];
                            for (int n = 0; n < size; ++n)
                            {
                                byte nyb = raw[n];
                                tmp[n * 2] = (byte)((nyb >> 4) & 0x0F);
                                tmp[n * 2 + 1] = (byte)(nyb & 0x0F);
                            }
                            raw = tmp;
                        }

                        // convert *unsigned* → *signed* if the attribute says so
                        if (!signed)
                        {
                            for (int n = 0; n < raw.Length; ++n)
                                raw[n] = (byte)(raw[n] - 128);
                        }

                        // Nominal playback rate =   MFP 2 456 kHz / (TP × TC)
                        // We only store the divider pair so the uploader can decide.
                        ushort timerCount = Swap(br.ReadUInt16());
                        byte timerPre = br.ReadByte();
                        double nominalHz = 2_457_600.0 /
                                            (PreDivToFactor(timerPre) * timerCount);

                        _digiDrumList.Add(new DigiDrumSample(raw, nominalHz));
                    }
                }

                if (extra > 0)
                    br.BaseStream.Seek(extra, SeekOrigin.Current);

                _isYM6 = _type == "YM6!";

                raw = br.ReadBytes(_frameCount * 16);
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

        private static int PreDivToFactor(int tp) => tp switch
        {
            1 => 4,
            2 => 10,
            3 => 16,
            4 => 50,
            5 => 64,
            6 => 100,
            7 => 200,
            _ => 1
        };

        private string ReadNullString(BinaryReader br)
        {
            var sb = new StringBuilder();
            byte b;
            while ((b = br.ReadByte()) != 0)
                sb.Append((char)b);
            return sb.ToString();
        }

        public IEnumerable<Effect> GetEffects(int frame)
        {
            if (!_isYM6) yield break;

            var e1 = DecodeEffect(frame, 1, 6, 14);
            if (e1 != null) yield return e1;

            var e2 = DecodeEffect(frame, 3, 8, 15);
            if (e2 != null) yield return e2;
        }

        private Effect? DecodeEffect(int frame, int flagR, int timerR, int countR)
        {
            int idx = frame * 16;
            byte flag = _bytes[idx + flagR];

            int vBits = (flag >> 4) & 0x03;       // 00 = no effect
            if (vBits == 0) return null;
            int voice = vBits - 1;                // 0‑2

            int tp = (_bytes[idx + timerR] >> 5) & 0x07;
            int tc = _bytes[idx + countR];

            return flagR switch
            {
                1 => new SIDEffect(EffectType.SIDVoice, frame, voice, tp, tc, (flag & 0x40) != 0),
                3 => new DigiDrumEffect(EffectType.DigiDrum, frame, voice, tp, tc, _bytes[idx + 8 + voice] & 0x1F),
                _ => null
            };
        }

        public abstract record Effect   // immutable “value‑object” base
        {
            public EffectType Type { get; init; }  // type of effect
            public int Frame { get; init; }
            public int Voice { get; init; }     // 0 = A, 1 = B, 2 = C
            public int TimerDivisor { get; init; }     // TP  (0‑7)
            public int TimerCount { get; init; }     // TC  (0‑255)

            public Effect(EffectType type, int frame, int voice, int timerDivisor, int timerCount)
            {
                Type = type;
                Frame = frame;
                Voice = voice;
                TimerDivisor = timerDivisor;
                TimerCount = timerCount;
            }

            public override string ToString() =>
                $"{Type.ToString()} (Voice {Voice}, Div {TimerDivisor}, Count {TimerCount})";
        }

        public sealed record SIDEffect : Effect
        {
            public bool Restart { get; init; }           // r1.b6 flag
            public byte VMax => (byte)(TimerCount & 0x1F);

            public SIDEffect(EffectType type, int frame, int voice, int timerDivisor, int timerCount, bool restart)
                : base(type, frame, voice, timerDivisor, timerCount)
            {
                Restart = restart;
            }

            public override string ToString() =>
                $"{Type.ToString()} (Voice {Voice}, Div {TimerDivisor}, Count {TimerCount} Restart {Restart})";
        }

        public sealed record DigiDrumEffect : Effect
        {
            public int SampleIndex { get; init; }        // taken from r8/r9/r10 LSBs
                                                         // You could also store a pointer/offset into the sample table here
            public DigiDrumEffect(EffectType type, int frame,int voice, int timerDivisor, int timerCount, int sampleIndex)
                : base(type, frame,voice, timerDivisor, timerCount)
            {
                SampleIndex = sampleIndex;
            }

            public override string ToString() =>
                $"{Type.ToString()} (Voice {Voice}, Div {TimerDivisor}, Count {TimerCount} SampleIndex {SampleIndex})";
        }

        public sealed class DigiDrumSample
        {
            /// Raw sample bytes (8‑bit PCM, *signed* if attribute b1 is set,
            /// otherwise unsigned).  They are always stored in Motorola order.
            public readonly byte[] Data;

            /// Nominal replay frequency in Hz,    –► see YM spec: samples are
            /// replayed through MFP timer so they are *always* 8‑bit mono PCM.
            public readonly double NominalHz;

            public DigiDrumSample(byte[] data, double nominalHz)
            {
                Data = data;
                NominalHz = nominalHz;
            }

            public string ToString()
            {
                string dataInfo = Data.Length > 16
                    ? $"{Data.Take(16).Select(b => b.ToString("X2")).Aggregate((a, b) => a + " " + b)} ..."
                    : string.Join(" ", Data.Select(b => b.ToString("X2")));

                return $"DigiDrumSample (NominalHz: {NominalHz}Hz, Data: {dataInfo} Length: {Data.Length} bytes)";
            }
        }

        private static uint Swap(uint v) =>
            (v >> 24) | ((v >> 8) & 0x0000FF00) | ((v << 8) & 0x00FF0000) | (v << 24);

        private static ushort Swap(ushort v) => (ushort)(((v >> 8) & 0x00FF) | ((v << 8) & 0xFF00));

        public IReadOnlyList<DigiDrumSample> DigiDrums => _digiDrumList;
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
