using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace YMPlayer
{
    public class YMModule
    {
        public YMParser[] Parsers { get; } = new YMParser[3];

        public int FrameCount => Parsers[0]?.FrameCount ?? 0;
        public int FrameRate => Parsers[0]?.FrameRate ?? 50;
        public int FrameLoop => Parsers[0]?.FrameLoop ?? 0;
        public TimeSpan TotalTime => Parsers[0]?.TotalTime ?? TimeSpan.Zero;

        public YMModule(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                var match = Regex.Match(file, @"\.(\d)\.ym$", RegexOptions.IgnoreCase);
                int index = match.Success ? int.Parse(match.Groups[1].Value) - 1 : 0;
                Parsers[index] = new YMParser(file);
            }
        }

        public void SendFrame(int frameIndex, Action<int, byte[], int> sendRegisters)
        {
            for (int chip = 0; chip < 3; chip++)
            {
                if (Parsers[chip] != null && frameIndex < Parsers[chip].FrameCount)
                    sendRegisters(chip, Parsers[chip].Bytes, frameIndex);
            }
        }

        public void OutputInfo()
        {
            for (int i = 0; i < 3; i++)
            {
                if (Parsers[i] == null) continue;
                Console.WriteLine($"--- Chip {i} ---");
                Console.WriteLine("FileName: " + Parsers[i].FileName);
                Console.WriteLine("Type: " + Parsers[i].Type);
                Console.WriteLine("Frame Count: " + Parsers[i].FrameCount);
                Console.WriteLine("Song Attributes: " + Parsers[i].SongAttributes);
                Console.WriteLine("Digidrums Samples: " + Parsers[i].DigidrumsSamples);
                Console.WriteLine("YM Frequency: " + Parsers[i].YmFrequency + "Hz");
                Console.WriteLine("Frame Rate: " + Parsers[i].FrameRate + "Hz");
                Console.WriteLine("Loop Frame Number: " + Parsers[i].FrameLoop);
                Console.WriteLine("Title: " + Parsers[i].Title);
                Console.WriteLine("Artist: " + Parsers[i].Artist);
                Console.WriteLine("Comments: " + Parsers[i].Comments);
                Console.WriteLine();
            }
        }
    }
}
