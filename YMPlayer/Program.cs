using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace YMPlayer
{
    class Program
    {
        private static YMParser _ymParser = null;
        private static SerialPort _serialPort = null;
        private static FramePump _pump;

        private static string[] _songArray = null;
        private static int _songIndex = 0;
        private static int _frameIndex = 0;

        private static byte[] _emptyRegisters = new byte[16];

        private const int PACKET_SIZE = 16;

        public static void Main(string[] args)
        {
            string startupPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string songsPath = Path.Combine(startupPath, "Songs");

            _songArray = Directory.GetFiles(songsPath, "*.YM");
            /* _songArray = new string[]
            {
                Path.Combine(songsPath, "LEDSTRM2.YM")
            }; */

            Random random = new Random();
            _songArray = _songArray.OrderBy(x => random.Next()).ToArray();

            Console.WriteLine("YMPlayer, simple streamer for YM2149.");
            Console.WriteLine("Press a key to Exit.");

            Console.WriteLine("Opening serial port");
            //_serialPort = new SerialPort("COM4", 115200)
            _serialPort = new SerialPort("COM7", 2_000_000)
            {
                WriteTimeout = 100,
                Handshake = Handshake.None
            };
            _serialPort.Open();

            SendRegisters(0, _emptyRegisters);
            SendRegisters(1, _emptyRegisters);
            SendRegisters(2, _emptyRegisters);

            _ymParser = new YMParser(_songArray[_songIndex]);

            OutputYmInfo();

            StartPlayer();

            Console.ReadKey();

            if (_pump != null)
            {
                _pump.Dispose();
                _pump = null;
            }

            if (_serialPort != null)
            {
                SendRegisters(0, _emptyRegisters);
                SendRegisters(1, _emptyRegisters);
                SendRegisters(2, _emptyRegisters);

                _serialPort.Dispose();
                _serialPort = null;
            }
        }

        static void StartPlayer()
        {
            _frameIndex = 0;
            _pump?.Dispose();
            _pump = new FramePump(_ymParser.FrameRate, OnFrame);
        }

        static void SendRegisters(int chipIndex, byte[] registers, int frameIndex = 0)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                return;

            byte[] data = new byte[17];
            data[0] = (byte)chipIndex;
            Array.Copy(registers, frameIndex * PACKET_SIZE, data, 1, PACKET_SIZE);

            _serialPort.Write(data, 0, PACKET_SIZE + 1);
        }

        static void OnFrame()
        {
            SendRegisters(0, _ymParser.Bytes, _frameIndex);

            TimeSpan timeSpan = TimeSpan.FromSeconds((double)(_frameIndex + 1) / _ymParser.FrameRate);

            string cur = timeSpan.ToString(@"mm\:ss\.ff");
            string total = _ymParser.TotalTime.ToString(@"mm\:ss\.ff");

            Console.Write($"\r{cur}/{total}  -  Frame {_frameIndex + 1}/{_ymParser.FrameCount}    ");

            if (++_frameIndex >= _ymParser.FrameCount)
            {
                _frameIndex = _ymParser.FrameLoop;

                if (++_songIndex == _songArray.Length)
                    _songIndex = 0;

                SendRegisters(0, _emptyRegisters);
                SendRegisters(1, _emptyRegisters);
                SendRegisters(2, _emptyRegisters);

                _ymParser = new YMParser(_songArray[_songIndex]);

                OutputYmInfo();

                StartPlayer();
            }
        }

        private static void OutputYmInfo()
        {
            Console.WriteLine("--------------------------------------------");
            Console.WriteLine("Type: " + _ymParser.Type);
            Console.WriteLine("Frame Count: " + _ymParser.FrameCount);
            Console.WriteLine("Song Attributes: " + _ymParser.SongAttributes);
            Console.WriteLine("Digidrums Samples: " + _ymParser.DigidrumsSamples);
            Console.WriteLine("YM Frequency: " + _ymParser.YmFrequency + "Hz");
            Console.WriteLine("Frame Rate: " + _ymParser.FrameRate + "Hz");
            Console.WriteLine("Loop Frame Number: " + _ymParser.FrameLoop);
            Console.WriteLine();
            Console.WriteLine("Title: " + _ymParser.Title);
            Console.WriteLine("Artist: " + _ymParser.Artist);
            Console.WriteLine("Comments: " + _ymParser.Comments);
        }
    }
}
