/*
 * Ym2149Synth
 * http://trash80.com
 * Copyright (c) 2016 Timothy Lamb
 *
 * This file is part of Ym2149Synth.
 *
 * Ym2149Synth is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * Ym2149Synth is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with Foobar.  If not, see <http://www.gnu.org/licenses/>.
 *
 */

/*
 * Define BENCHMARK to run benchmarking (Look to method "benchmark" SynthController.cpp)
 */
//#define BENCHMARK 1
#define YMPLAYER 1

#include <TimerOne.h>
#include "YM2149.h"
//#include "MidiDeviceUsb.h"
#include "MidiDeviceSerial.h"
#include "SynthController.h"
#include "YMPlayerSerial.h"

SynthController synth;

#ifdef YMPLAYER
YMPlayerSerial ymPlayer;
#else
MidiDeviceSerial midi(&Serial1);
//MidiDeviceUsb usbMidi;
#endif

//IntervalTimer samplerTimer;
//IntervalTimer eventTimer;

const float sampleRate = 22050;
const float softSynthTimer = 1000000L / sampleRate;

const unsigned long sidVoiceFreq = 8000; // frequency in Hz
const unsigned long sidTimer = 1000000UL / sidVoiceFreq;

void updateEffects()
{
#ifdef YMPLAYER
    ymPlayer.updateEffects();
#endif
}

ISR(TIMER1_COMPA_vect)
{
#ifdef YMPLAYER
    ymPlayer.updateEffects();
#endif
}

void initEffectsTimer()
{
    cli();                      // disable IRQ while we mangle Timer1
    TCCR1A = 0;
    TCCR1B = _BV(WGM12) | _BV(CS10);      // CTC, prescaler 1
    OCR1A  = (ISR_PERIOD_US * (F_CPU/1000000UL)) - 1;
    TIMSK1 = _BV(OCIE1A);                 // enable compare‑A ISR
    sei();
}

void updateSoftSynth()
{
    synth.updateSoftSynths();
}

void updateEvents()
{
    synth.updateEvents();
}

void setup()
{
#ifdef YMPLAYER
    ymPlayer.begin();

    //Timer1.initialize(ISR_PERIOD_US);
    //Timer1.attachInterrupt(updateEffects);
    //initEffectsTimer();
#else
    synth.setChannels(1, 2, 3);
    synth.begin();

    //usbMidi.setCallback(&synth);
    midi.setCallback(&synth);
    midi.begin();
#ifndef BENCHMARK
    Timer1.initialize(softSynthTimer); // in microseconds
    Timer1.attachInterrupt(updateSoftSynth);
    //samplerTimer.begin(updateSoftSynth, softSynthTimer);
    //samplerTimer.priority(0);
    //eventTimer.begin(updateEvents, 1000);
    //eventTimer.priority(1);
#endif
#endif
}

void loop()
{
#ifdef YMPLAYER
    ymPlayer.update();
#else
#ifndef BENCHMARK
    midi.update();

    // Handle events every 1ms
    static unsigned long lastEventTime = 0;
    if (millis() - lastEventTime >= 1) {
        updateEvents();
        lastEventTime = millis();
    }

    //usbMidi.update();
#else
    synth.benchmark();
#endif
#endif
}
