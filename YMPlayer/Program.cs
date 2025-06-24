// benbaker76 (https://github.com/benbaker76)

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
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using static YMPlayer.YMParser;

namespace YMPlayer
{
    class Program
    {
        private static YMModule _ymModule = null;
        private static SerialPort _serialPort = null;
        private static FramePump _pump;

        private static string[][] _modules;
        private static int _songIndex = 0;
        private static int _frameIndex = 0;

        private static byte[] _emptyRegisters = new byte[16];

        private const int PACKET_SIZE = 16;

        public static void Main(string[] args)
        {
            string startupPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string songsPath = Path.Combine(startupPath, "Songs");

            string[] fileArray = Directory.GetFiles(songsPath, "*.ym");
            /* _songArray = new string[]
            {
                Path.Combine(songsPath, "LEDSTRM2.YM")
            }; */

            Random random = new Random();
            _modules = fileArray
                .GroupBy(path => Regex.Replace(Path.GetFileName(path), @"\.\d\.ym$", "", RegexOptions.IgnoreCase))
                .Select(g => g.OrderBy(f => f).ToArray())
                .ToArray();

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

            _songIndex = random.Next(_modules.Length);

            _ymModule = new YMModule(_modules[_songIndex]);

            _ymModule.UploadDigiDrums();
            _ymModule.OutputInfo();

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
            _pump = new FramePump(_ymModule.FrameRate, OnFrame);
        }

        static void HandleEffect(Effect fx)
        {
            switch (fx.Type)
            {
                case EffectType.SIDVoice:
                    Console.WriteLine(((SIDEffect)fx).ToString());
                    //TimerSynth.SetVoice(fx.Voice, fx.TimerDivisor, fx.TimerCount);
                    break;
                case EffectType.DigiDrum:
                    Console.WriteLine(((DigiDrumEffect)fx).ToString());
                    //DigiDrum.Trigger(fx.Voice, fx.TimerDivisor, fx.TimerCount);
                    break;
            }
        }

        static void SendRegisters(int chipIndex, byte[] registers, int frameIndex = 0)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                return;

            byte[] data = new byte[17];
            data[0] = (byte)chipIndex;
            Array.Copy(registers, frameIndex * PACKET_SIZE, data, 1, PACKET_SIZE);

            // Write bytes in hex to console for debugging
            //string hex = BitConverter.ToString(data).Replace("-", " ");
            //Console.WriteLine($"Chip: {chipIndex} Frame: {frameIndex}: {hex}");

            _serialPort.Write(data, 0, PACKET_SIZE + 1);

            /* if (_ymModule == null)
                return;

            var effects = _ymModule.GetEffects(chipIndex, frameIndex);

            if (effects != null)
            {
                foreach (var fx in effects)
                    HandleEffect(fx);
            } */
        }

        static void OnFrame()
        {
            _ymModule.SendFrame(_frameIndex, SendRegisters);

            TimeSpan timeSpan = TimeSpan.FromSeconds((double)(_frameIndex + 1) / _ymModule.FrameRate);
            string cur = timeSpan.ToString(@"mm\:ss\.ff");
            string total = _ymModule.TotalTime.ToString(@"mm\:ss\.ff");

            Console.Write($"\r{cur}/{total}  -  Frame {_frameIndex + 1}/{_ymModule.FrameCount}    ");

            if (++_frameIndex >= _ymModule.FrameCount)
            {
                _frameIndex = _ymModule.FrameLoop;

                if (++_songIndex == _modules.Length)
                    _songIndex = 0;

                for (int i = 0; i < 3; i++)
                    SendRegisters(i, _emptyRegisters);

                _ymModule = new YMModule(_modules[_songIndex]);

                _ymModule.UploadDigiDrums();
                _ymModule.OutputInfo();

                StartPlayer();
            }
        }
    }
}
