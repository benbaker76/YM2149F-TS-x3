// benbaker76 (https://github.com/benbaker76)

#include "YMPlayerSerial.h"
//#include "DigiDrum.h"

// http://leonard.oxg.free.fr/ymformat.html
// http://lynn3686.com/ym3456_tidy.html

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
void YMPlayerSerialClass::decodeEffect(uint8_t       chip,
                                        const uint8_t regs[16],
                                        uint8_t       flagR,
                                        uint8_t       timerR,
                                        uint8_t       countR)
{
    const uint8_t flag = regs[flagR];

    /*---------------- voice selection (00 = none) ---------------*/
    const uint8_t vBits = (flag >> 4) & 0x03;      // r?.5‑4
    if (vBits == 0) return;                        // nothing in this slot
    const uint8_t v = vBits - 1;                  // 0=A,1=B,2=C

    /*---------------- which effect lives in this slot? ----------*/
    EffectType type =
        (flagR == 1) ? EffectType::TimerSynth     // slot‑1  (R1)
      : (flagR == 3) ? EffectType::DigiDrum       // slot‑2  (R3)
                     : EffectType::None;

    /*---------------- timer parameters (ST MFP) -----------------*/
    const uint8_t tp     = (regs[timerR] >> 5) & 0x07;    // TP bits 7‑5
    const uint8_t tc     =  regs[countR];                 // 8‑bit count
    const uint32_t ticks = static_cast<uint32_t>(tc + 1) * tpMul[tp];

    /*------------------------------------------------------------*/
    if (type == EffectType::TimerSynth)          /* == SID square */
    {
        SidState &s = sid[chip][v];

        s.active  = true;
        s.level   = (tc & 0x1F) > 15 ? 15 : (tc & 0x1F);
        s.reload  = ticks;
        s.phase   = ticks;
        s.toggle  = 0;
        return;
    }

    if (type == EffectType::DigiDrum)
    {
        /* 5‑bit sample number is in the volume register of the voice */
        static const uint8_t volReg[3] = { 8, 9, 10 };
        const uint8_t sampleNo = regs[volReg[v]] & 0x1F;

        /* dd[...] handling not shown – identical pattern to SidState */
        (void)sampleNo;      // placeholder to silence “unused” warning
        return;
    }
}

void YMPlayerSerialClass::update()
{
    const int packetSize = 17;
    byte buffer[packetSize];

    size_t bytesRead = Serial.readBytes((char*)buffer, packetSize);

    /* static bool played = false;
    if (!played) {
        Ym.setPortIO(0, 1, 1);
        Ym.setTone(0, 0, 300);           // Voice A, 300 period
        Ym.setVolume(0, 0, 0x0F);        // Max volume
        Ym.setNoise(0, 0, 0);            // Enable tone only
        played = true;
    }

    return; */

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
#if USE_BATCH_ISR == 0                 /* ========= 4 µs ISR ========= */

    /* loop over all chips / voices every 4 µs ----------------------- */
    for (uint8_t c = 0; c < 3; ++c) {
        // select chip once
        if (c != Ym.currentChip) {
            Ym.selectYM(c);
            Ym.currentChip = c;
        }

        for (uint8_t v = 0; v < 3; ++v) {
            /* -------- SID Voice -------- */
            SidState &s = sid[c][v];
            if (s.active && --s.phase == 0) {
                s.phase  = s.reload;
                s.toggle ^= 1;
                Ym.writeFast(YM2149::REG_A_LEVEL + v,
                                     s.toggle ? s.level : 0);
            }

            /* -------- Digi‑Drum -------- */
            DigiDrumState &d = dd[c][v];
            if (!d.active) continue;
            if (--d.phase) continue;

            d.phase = d.reload;
            uint8_t sampleByte = sampleAddress[d.sample][d.pos++];
            if (d.pos >= sampleLen[d.sample]) d.active = false;

            Ym.writeFast(YM2149::REG_A_LEVEL + v, sampleByte & 0x0F);
        }
    }

#else                                   /* ========= 32 µs ISR ======= */

    /* 1) update countdowns (logic) --------------------------------- */
    for (uint8_t c = 0; c < 3; ++c)
        for (uint8_t v = 0; v < 3; ++v) {
            SidState &s = sid[c][v];
            if (s.active) {
                if (s.phase > TICKS_ISR) s.phase -= TICKS_ISR;
                else { s.phase += s.reload - TICKS_ISR; s.toggle ^= 1; }
            }

            /* DigiDrumState &d = dd[c][v];
            if (d.active) {
                if (d.phase > TICKS_ISR) d.phase -= TICKS_ISR;
                else {
                    d.phase = d.reload;
                    ++d.pos;
                    if (d.pos >= sampleLen[d.sample]) d.active = false;
                }
            } */
        }

    /* 2) physical writes – one every 4 µs -------------------------- */
    static uint8_t slice = 0;               // 0‑7  (advances each ISR)

    for (uint8_t c = 0; c < 3; ++c) {
        /* ---- change chip only when we really have to ------------ */
        if (c != Ym.currentChip) {
            Ym.selectYM(c);
            Ym.currentChip = c;
        }

        for (uint8_t v = 0; v < 3; ++v) {
            if (((c * 3 + v) & 7) != slice) continue;   // not this 4‑µs slot

            /* ---- SID write (takes priority over DD) ------------- */
            SidState &s = sid[c][v];
            if (s.active) {
                Ym.writeFast(YM2149::REG_A_LEVEL + v,
                                     s.toggle ? s.level : 0);
                continue;   // DD uses same register – skip this slot
            }

            /* ---- Digi‑Drum write -------------------------------- */
            /* DigiDrumState &d = dd[c][v];
            if (d.active) {
                uint8_t sampleByte = sampleAddress[d.sample][d.pos];
                Ym.writeFast(YM2149::REG_A_LEVEL + v,
                                     sampleByte & 0x0F);
            } */
        }
    }
    slice = (slice + 1) & 0x07;             // next 4‑µs sub‑slot

#endif  /* USE_BATCH_ISR */
}
