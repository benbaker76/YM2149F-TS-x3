// Original code by Mr Megahertz
// Url: https://pastebin.com/54nA1EaH
// Updates by headkaze

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
            LhaFile lhaFile = new LhaFile(fileName, Encoding.UTF8);
            LhaEntry lhaEntry = lhaFile.GetEntry(0);
            byte[] bytes = lhaFile.GetEntryBytes(lhaEntry);
            lhaFile.Close();

            using (MemoryStream memoryStream = new MemoryStream(bytes))
            {
                using (BinaryReader binaryReader = new BinaryReader(memoryStream))
                {
                    // Header
                    ASCIIEncoding encoding = new ASCIIEncoding();

                    _type = encoding.GetString(binaryReader.ReadBytes(4));
                    string checkString = encoding.GetString(binaryReader.ReadBytes(8));

                    if (!checkString.Equals("LeOnArD!"))
                        return;

                    _frameCount = (int)SwapByteOrder(binaryReader.ReadUInt32());
                    _songAttributes = SwapByteOrder(binaryReader.ReadUInt32());
                    _digidrumsSamples = SwapByteOrder(binaryReader.ReadUInt16());
                    _ymFrequency = SwapByteOrder(binaryReader.ReadUInt32());
                    _frameRate = SwapByteOrder(binaryReader.ReadUInt16());
                    _frameLoop = (int)SwapByteOrder(binaryReader.ReadUInt32());

                    binaryReader.ReadUInt16(); // Unused bytes

                    // Song Info
                    _title = ReadNullTerminationString(binaryReader, encoding);
                    _artist = ReadNullTerminationString(binaryReader, encoding);
                    _comments = ReadNullTerminationString(binaryReader, encoding);

                    // Read interleaved frames
                    _bytes = ReadAllFrames(binaryReader, _frameCount);
                }
            }

            _totalTime = TimeSpan.FromSeconds((_frameCount / _frameRate));
        }

        private byte[] ReadAllFrames(BinaryReader reader, int frameCount)
        {
            int totalBytes = 16 * frameCount;
            int currentRegisterStreamSize = totalBytes / 16;

            byte[] tempArray = new byte[totalBytes];
            byte[] finalArray = new byte[totalBytes];

            tempArray = reader.ReadBytes(totalBytes);

            byte currentValue;

            for (int currentRegister = 0; currentRegister < 16; currentRegister++)
            {

                int offset = (currentRegisterStreamSize * (currentRegister));
                for (int i = 0; i < currentRegisterStreamSize; i++)
                {
                    currentValue = tempArray[i + offset];
                    finalArray[16 * i + currentRegister] = currentValue;
                }
            }
            return finalArray;
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
