// benbaker76 (https://github.com/benbaker76)

#ifndef YMPlayerSerial_h
#define YMPlayerSerial_h

#include "Arduino.h"
#include "YM2149.h"

struct SidState {
    volatile bool     active  = false;
    volatile uint8_t  level   = 0;     // 0‑15
    volatile uint16_t reload  = 0;     // ticks of   4 µs
    volatile uint16_t phase   = 0;     // countdown  4 µs
    volatile uint8_t  toggle  = 0;     // 0 / 1
};
extern SidState sid[3][3];

struct DigiDrumState {
    volatile bool     active  = false;
    volatile uint16_t reload  = 0;      // 4 µs ticks
    volatile uint16_t phase   = 0;      // countdown (4 µs)
    volatile uint16_t pos     = 0;      // sample cursor
    volatile uint8_t  sample  = 0;      // sample # 0‑31
};
extern DigiDrumState dd[3][3];

// ----------------------------------------------------------
// Fast-SID ISR – choose ONE:
//    0  = 4 µs “every-tick” handler (slow serial, safe)
//    1  = 32 µs batched handler   (recommended)
// ----------------------------------------------------------
#define USE_BATCH_ISR   1
#if USE_BATCH_ISR
    constexpr uint8_t  TICK_US        = 4;
    constexpr uint8_t  ISR_PERIOD_US  = 32;
    constexpr uint8_t  TICKS_ISR      = ISR_PERIOD_US / TICK_US; // 8
#else
    constexpr uint8_t  TICK_US        = 4;
    constexpr uint8_t  ISR_PERIOD_US  = 4;
    constexpr uint8_t  TICKS_ISR      = 1;
#endif

enum class EffectType : uint8_t {
    None        = 255,
    SIDVoice    = 0,
    Digidrum    = 1,
    SinusSID    = 2,
    SyncBuzzer  = 3
};

class YMPlayerSerialClass {
  public:
    YMPlayerSerialClass() {};

    void begin();
    void update();
    void updateEffects();

  private:
    YM2149 Ym;

    void decodeEffect(uint8_t chip,
                      const uint8_t regs[16],
                      uint8_t flagR,
                      uint8_t timerR,
                      uint8_t countR);

    const uint8_t regMask[14] =
    {
        0xFF, 0x0F,   // R0,R1  A‑period
        0xFF, 0x0F,   // R2,R3  B‑period
        0xFF, 0x0F,   // R4,R5  C‑period
        0x1F,         // R6     Noise period
        0xFF,         // R7     Mixer
        0x0F,         // R8     A‑volume
        0x0F,         // R9     B‑volume
        0x0F,         // R10    C‑volume
        0xFF, 0xFF,   // R11,R12 Envelope period
        0x0F          // R13    Envelope shape
    };

    const uint16_t tpMul[8] =
    {        // pre‑div. → ticks of 4 µs
        4,   // 000 – (unused, timer stop)
        4,   // 001 – ÷4   → 1 × 4 µs
        10,  // 010 – ÷10
        16,  // 011 – ÷16
        50,  // 100 – ÷50
        64,  // 101 – ÷64
        100, // 110 – ÷100
        200  // 111 – ÷200
    };

    //─────────────── Enum & prototype ───────────────────────────────────────
    enum class EffectType : uint8_t { SIDVoice = 0, DigiDrum = 1, SinusSID = 2, SyncBuzzer = 3, None = 255 };
};

typedef YMPlayerSerialClass YMPlayerSerial;

#endif
