// benbaker76 (https://github.com/benbaker76)

#include "YMPlayerSerial.h"
#include "DigiDrum.h"

// http://leonard.oxg.free.fr/ymformat.html
// http://lynn3686.com/ym3456_tidy.html

SidState sid[3][3];
DigiDrumState dd[3][3];

void YMPlayerSerialClass::begin()
{
    Ym.begin();

    for(int i = 0; i < 3; i++)
    {
        Ym.setPortIO(i, 1, 1);     // Both ports as outputs
        Ym.setPin(i, 0, 1);        // A0 high
        Ym.mute(i);                // Mute this chip
    }

    //Serial.begin(115_200);
    Serial.begin(2000000);
}

// ──────────────────────────────────────────────────────────────────────────
// Decode one Timer‑Synth (SIDVoice) or Digi‑Drum slot and update globals
// ──────────────────────────────────────────────────────────────────────────
void YMPlayerSerialClass::decodeEffect(uint8_t chip, const uint8_t regs[16],
                                       uint8_t flagR, uint8_t timerR, uint8_t countR)
{
    uint8_t flag  = regs[flagR];
    uint8_t vBits = (flag >> 4) & 0x03;
    if (vBits == 0) return;

    uint8_t v = vBits - 1;
    EffectType type = (flagR == 1) ? EffectType::SIDVoice
                         : (flagR == 3) ? EffectType::DigiDrum
                         : EffectType::None;

    uint8_t tp = (regs[timerR] >> 5) & 0x07;
    uint8_t tc = regs[countR];
    uint32_t ticks = uint32_t(tc + 1) * tpMul[tp];

    if (type == EffectType::SIDVoice) {
        SidState &s = sid[chip][v];
        s.active = true;
        s.level  = min(tc & 0x1F, 15);
        s.reload = ticks;
        s.phase  = ticks;
        s.toggle = 0;
    }
    else if (type == EffectType::DigiDrum) {
        DigiDrumState &d = dd[chip][v];
        d.reload = ticks;
        d.phase  = ticks;
        d.pos    = 0;
        d.sample = regs[v + 8] & 0x1F;
    }
}

void YMPlayerSerialClass::update()
{
    const int packetSize = 17;
    byte buffer[packetSize];

    size_t bytesRead = Serial.readBytes((char*)buffer, packetSize);

    /* static bool played = false;
    if (!played) {
        // Compute tone period for 440 Hz
        uint16_t period = 2000000 / (16 * 440);

        // Once only (e.g., setup):
        Ym.setTone(0, 0, period);
        Ym.setTone(0, 1, period);
        Ym.setTone(0, 2, period);

        Ym.setVolume(0, 0, 0x08);
        Ym.setVolume(0, 1, 0x08);
        Ym.setVolume(0, 2, 0x08);

        Ym.setNoise(0, 0, 1); // tone only on channel A
        Ym.setNoise(0, 1, 1); // tone only on channel B
        Ym.setNoise(0, 2, 1); // tone only on channel C

        played = true;
    } */

    //return;

    if (bytesRead == packetSize)
    {
        uint8_t chip = buffer[0];

        if (chip > 2)
            return;

        Ym.setLED(chip, !Ym.getLED(chip));

        const uint8_t *regs = buffer + 1;

        // Send register data
        for (int i = 0; i < 14; i++)
            Ym.write(chip, i, regs[i] & regMask[i]);

        //static bool tick = false;
        //tick = !tick;
        //Ym.setVolume(0, 0, tick ? 0x0F : 0x00);
        //Ym.setVolume(0, 1, tick ? 0x0F : 0x00);
        //Ym.setVolume(0, 2, tick ? 0x0F : 0x00);

        decodeEffect(chip, regs, /*flagR*/1, /*timerR*/6,  /*countR*/14);
        decodeEffect(chip, regs, /*flagR*/3, /*timerR*/8,  /*countR*/15);
    }
}

/* -----------------------------------------------------------------------
 * updateEffects()
 *  • Called from the Timer‑1 ISR
 *  • Keeps SID‑Voice and Digi‑Drum running between serial frames
 *  • Works in two modes:
 *        USE_BATCH_ISR == 0  →  4 µs interrupt, touch bus every call
 *        USE_BATCH_ISR == 1  →  32 µs interrupt, stagger bus writes
 * -------------------------------------------------------------------- */
void YMPlayerSerialClass::updateEffects()
{
#if USE_BATCH_ISR == 0                 // ========= 4 µs ISR =========

    // loop over all chips / voices every 4 µs -----------------------
    for (uint8_t c = 0; c < 3; ++c) {
        // select chip once
        if (c != Ym.currentChip) {
            Ym.selectYM(c);
            Ym.currentChip = c;
        }

        for (uint8_t v = 0; v < 3; ++v) {
            //  -------- SID Voice --------
            SidState &s = sid[c][v];
            if (s.active && --s.phase == 0) {
                s.phase  = s.reload;
                s.toggle ^= 1;
                Ym.writeFast(YM2149::REG_A_LEVEL + v,
                                     s.toggle ? s.level : 0);
            }

            //  -------- Digi‑Drum --------
            DigiDrumState &d = dd[c][v];
            if (!d.active) continue;
            if (--d.phase) continue;

            d.phase = d.reload;
            uint8_t sampleByte = sampleAddress[d.sample][d.pos++];
            if (d.pos >= sampleLen[d.sample]) d.active = false;

            Ym.writeFast(YM2149::REG_A_LEVEL + v, sampleByte & 0x0F);
        }
    }

#else                                   //  ========= 32 µs ISR =======

    //  1) update countdowns (logic) ---------------------------------
    for (uint8_t c = 0; c < 3; ++c)
        for (uint8_t v = 0; v < 3; ++v) {
            SidState &s = sid[c][v];
            if (s.active) {
                if (s.phase > TICKS_ISR) s.phase -= TICKS_ISR;
                else { s.phase += s.reload - TICKS_ISR; s.toggle ^= 1; }
            }

            DigiDrumState &d = dd[c][v];
            if (d.active) {
                if (d.phase > TICKS_ISR) d.phase -= TICKS_ISR;
                else {
                    d.phase = d.reload;
                    ++d.pos;
                    if (d.pos >= sampleLen[d.sample]) d.active = false;
                }
            }
        }

    //  2) physical writes – one every 4 µs --------------------------
    static uint8_t slice = 0;               // 0‑7  (advances each ISR)

    for (uint8_t c = 0; c < 3; ++c) {
        // ---- change chip only when we really have to ------------
        if (c != Ym.currentChip) {
            Ym.selectYM(c);
            Ym.currentChip = c;
        }

        for (uint8_t v = 0; v < 3; ++v) {
            if (((c * 3 + v) & 7) != slice) continue;   // not this 4‑µs slot

            //  ---- SID write (takes priority over DD) -------------
            SidState &s = sid[c][v];
            if (s.active) {
                Ym.writeFast(YM2149::REG_A_LEVEL + v,
                                     s.toggle ? s.level : 0);
                continue;   // DD uses same register – skip this slot
            }

            // ---- Digi‑Drum write --------------------------------
            DigiDrumState &d = dd[c][v];
            if (d.active) {
                uint8_t sampleByte = sampleAddress[d.sample][d.pos];
                Ym.writeFast(YM2149::REG_A_LEVEL + v,
                                     sampleByte & 0x0F);
            }
        }
    }
    slice = (slice + 1) & 0x07;             // next 4‑µs sub‑slot

#endif  // USE_BATCH_ISR
}
