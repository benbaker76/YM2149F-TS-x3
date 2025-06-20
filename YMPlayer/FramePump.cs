using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YMPlayer
{
    public class FramePump : IDisposable
    {
        private readonly Thread _thread;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly int _framePeriodUs;
        private readonly Action _tick;

        public FramePump(int frameRate, Action tick)
        {
            _framePeriodUs = 1_000_000 / frameRate;   // 50 Hz -> 20 000 µs
            _tick = tick;
            _thread = new Thread(Run) { Priority = ThreadPriority.Highest };
            _thread.Start();
        }

        private void Run()
        {
            var sw = Stopwatch.StartNew();
            long nextUs = 0;

            while (!_cts.IsCancellationRequested)
            {
                long nowUs = sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;

                long diff = nextUs - nowUs;
                if (diff <= 0)
                {
                    _tick();                 // send one 16-byte frame
                    nextUs += _framePeriodUs;
                    continue;
                }

                if (diff > 2000)            // >2 ms early? yield CPU
                    Thread.Sleep(1);
                else
                    Thread.SpinWait(75);     // busy-wait ≈0.1 ms
            }
        }

        public void Dispose() => _cts.Cancel();
    }
}
