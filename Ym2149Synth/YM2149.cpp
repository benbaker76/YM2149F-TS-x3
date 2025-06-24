#include "YM2149.h"
#include <util/atomic.h>
#include <avr/io.h>

constexpr uint8_t LED_BIT[3] = {
    _BV(PB1),  // D15 → PB1 
    _BV(PB3),  // D14 → PB3
    _BV(PB2)   // D16 → PB2
};

volatile uint8_t YM2149Class::currentChip = 0;

void YM2149Class::begin()
{
    currentChip = 255;

    DDRD |= _BV(PD0) | _BV(PD1) | _BV(PD4) | _BV(PD7); // D2, D3, D4, D6
    DDRB |= _BV(PB4) | _BV(PB5);                       // D8, D9
    DDRC |= _BV(PC6);                                  // D5
    DDRE |= _BV(PE6);                                  // D7

    // --- Control pins ---
    // BC1 = D10 → PB6
    // BDIR = D20 → PF5
    DDRB |= _BV(PB6);
    DDRF |= _BV(PF5);
    PORTB &= ~_BV(PB6);
    PORTF &= ~_BV(PF5);

    // --- Control lines ---
    DDRB |= _BV(PB6);         // BC1 (D10) = OUTPUT
    DDRF |= _BV(PF5);         // BDIR (D20) = OUTPUT

    PORTB &= ~_BV(PB6);       // BC1 LOW
    PORTF &= ~_BV(PF5);       // BDIR LOW

    // --- Chip select lines ---
    DDRF |= _BV(PF4) | _BV(PF6) | _BV(PF7);  // SEL_A (A3), SEL_B (A1), SEL_C (A0)
    PORTF &= ~(_BV(PF4) | _BV(PF6) | _BV(PF7)); // Set all low initially

    // --- ENABLE pin (A2 = PF5) ---
    PORTF |= _BV(PF5);        // Drive high to enable chips

    // --- LED pins (PB1, PB3, PB2 = D15, D14, D16) ---
    DDRB |= _BV(PB1) | _BV(PB3) | _BV(PB2);   // Set as outputs
    PORTB |= _BV(PB1) | _BV(PB2) | _BV(PB3);  // Set HIGH = OFF if active LOW LEDs
}

void YM2149Class::selectYM(uint8_t chip)
{
    chip = 2 - chip;

    PORTF &= ~(SEL_A_BIT | SEL_B_BIT | SEL_C_BIT);

    if (chip & 1) PORTF |= SEL_A_BIT;
    if (chip & 2) PORTF |= SEL_B_BIT;
    if (chip & 4) PORTF |= SEL_C_BIT;
}

void YM2149Class::busWrite(uint8_t value)
{
    // --- Build PORTD value (PD0, PD1, PD4, PD7) ---
    uint8_t portd_mask = _BV(PD0) | _BV(PD1) | _BV(PD4) | _BV(PD7);
    uint8_t portd_val  =
        ((value & (1 << 0)) ? _BV(PD1) : 0) |
        ((value & (1 << 1)) ? _BV(PD0) : 0) |
        ((value & (1 << 2)) ? _BV(PD4) : 0) |
        ((value & (1 << 4)) ? _BV(PD7) : 0);
    PORTD = (PORTD & ~portd_mask) | portd_val;

    // --- Build PORTC value (PC6) ---
    PORTC = (PORTC & ~_BV(PC6)) | ((value & (1 << 3)) ? _BV(PC6) : 0);

    // --- Build PORTE value (PE6) ---
    PORTE = (PORTE & ~_BV(PE6)) | ((value & (1 << 5)) ? _BV(PE6) : 0);

    // --- Build PORTB value (PB4, PB5) ---
    uint8_t portb_mask = _BV(PB4) | _BV(PB5);
    uint8_t portb_val  =
        ((value & (1 << 6)) ? _BV(PB4) : 0) |
        ((value & (1 << 7)) ? _BV(PB5) : 0);
    PORTB = (PORTB & ~portb_mask) | portb_val;
}

void YM2149Class::writeFast(uint8_t address, uint8_t value)
{
    busWrite(address);             // 4
    PORTB |=  _BV(PB6);            // 1 – BC1
    PORTF |=  _BV(PF5);            // 1 – BDIR
    _NOP(); _NOP(); _NOP(); _NOP();// 4 – 250 ns
    PORTF &= ~_BV(PF5);            // 1 – BDIR
    PORTB &= ~_BV(PB6);            // 1 – BC1
    busWrite(value);               // 4
    PORTF |=  _BV(PF5);            // 1 – data strobe
    PORTF &= ~_BV(PF5);            // 1 – finish
}

void YM2149Class::write(uint8_t chip, uint8_t reg, uint8_t val)
{
    ATOMIC_BLOCK(ATOMIC_RESTORESTATE)
    {
        if (chip != currentChip)
        {
            selectYM(chip);
            currentChip = chip;
        }

        busWrite(reg & 0x1F);
        PORTB |= _BV(PB6);   // BC1 = HIGH
        PORTF |= _BV(PF5);   // BDIR = HIGH

        // Give the YM2149 time to latch register number
        _NOP(); _NOP(); _NOP(); _NOP(); // 4 cycles ≈ 250 ns

        PORTF &= ~_BV(PF5);  // BDIR = LOW
        PORTB &= ~_BV(PB6);  // BC1 = LOW

        _NOP(); _NOP(); _NOP(); _NOP(); // inter-phase delay

        busWrite(val);
        PORTF |= _BV(PF5);   // BDIR = HIGH
        _NOP(); _NOP(); _NOP(); _NOP();
        PORTF &= ~_BV(PF5);  // BDIR = LOW
    }
}

void YM2149Class::setPin(uint8_t chip, uint8_t pin, bool value)
{
    pin &= 0x0F;
    uint8_t mask = 1 << (pin & 0x07);

    if (pin >= 8)
    {
        // Port B
        if (value)
            portBValue[chip] |= mask;
        else
            portBValue[chip] &= ~mask;

        write(chip, REG_DATAPORT_B, portBValue[chip]);
    }
    else
    {
        // Port A
        if (value)
            portAValue[chip] |= mask;
        else
            portAValue[chip] &= ~mask;

        write(chip, REG_DATAPORT_A, portAValue[chip]);
    }
}

uint8_t YM2149Class::getPin(uint8_t chip, uint8_t pin)
{
    pin &= 0x0F;

    if (pin >= 8)
    {
        // Port B
        return (portBValue[chip] >> (pin - 8)) & 0x01;
    }
    else
    {
        // Port A
        return (portAValue[chip] >> pin) & 0x01;
    }
}

void YM2149Class::setPort(uint8_t chip, bool port, uint8_t value)
{
    if (port)
    {
        portBValue[chip] = value;
        write(chip, REG_DATAPORT_B, value);
    }
    else
    {
        portAValue[chip] = value;
        write(chip, REG_DATAPORT_A, value);
    }
}

uint8_t YM2149Class::getPort(uint8_t chip, bool port)
{
    return port ? portBValue[chip] : portAValue[chip];
}

void YM2149Class::setPortIO(uint8_t chip, bool portAOut, bool portBOut)
{
    // Bits 6 (port A) and 7 (port B) control I/O mode in the mixer register
    uint8_t portSettings = ((portBOut ? 1 : 0) << 7) | ((portAOut ? 1 : 0) << 6);
    mixerValue[chip] = (mixerValue[chip] & 0b00111111) | portSettings;
    write(chip, REG_MIXER, mixerValue[chip]);
}

void YM2149Class::setLED(uint8_t chip, bool state)
{
    ledState[chip] = state;
    if (state)
        PORTB |= LED_BIT[chip];    // turn LED ON
    else
        PORTB &= ~LED_BIT[chip];   // turn LED OFF
}

bool YM2149Class::getLED(uint8_t chip)
{
    return ledState[chip];
}

void YM2149Class::setNote(uint8_t chip, uint8_t voice, float midiNote)
{
    uint16_t freqVal;

    if (voice != 3)
    {
        // Convert MIDI note to frequency
        float freq = 440.0f * powf(2.0f, (midiNote - 69.0f) / 12.0f);

        if (voice == 4)
        {
            // Envelope frequency: 500kHz / (256 * freq)
            freqVal = static_cast<uint16_t>((YM_CLOCK_HZ / (256.0f * freq)) + 0.5f);
        }
        else
        {
            // Tone channels: 500kHz / (16 * freq)
            freqVal = static_cast<uint16_t>((YM_CLOCK_HZ / (16.0f * freq)) + 0.5f);
        }
    }
    else
    {
        // Noise: approximate mapping
        freqVal = constrain(static_cast<uint16_t>(31 - midiNote / 4), 0, 31);
    }

    setTone(chip, voice, freqVal);
}

void YM2149Class::setFreq(uint8_t chip, uint8_t voice, uint32_t freqHz)
{
    if (freqHz == 0) return; // avoid divide-by-zero

    uint16_t divisor = (voice == 4) ? 256 : 16; // 256 for envelope, 16 for tone
    uint16_t freqVal = static_cast<uint16_t>((YM_CLOCK_HZ / freqHz) / divisor + 0.5f);
    setTone(chip, voice, freqVal);
}

void YM2149Class::setTone(uint8_t chip, uint8_t voice, uint16_t value)
{
    switch (voice)
    {
        case 0: // Channel A
            write(chip, REG_A_FREQ,     value & 0xFF);
            write(chip, REG_A_FREQ + 1, (value >> 8) & 0x0F);
            break;

        case 1: // Channel B
            write(chip, REG_B_FREQ,     value & 0xFF);
            write(chip, REG_B_FREQ + 1, (value >> 8) & 0x0F);
            break;

        case 2: // Channel C
            write(chip, REG_C_FREQ,     value & 0xFF);
            write(chip, REG_C_FREQ + 1, (value >> 8) & 0x0F);
            break;

        case 3: // Noise (5 bits)
            write(chip, REG_NOISE_FREQ, value & 0x1F);
            break;

        case 4: // Envelope
            // IMPORTANT: value must already be calculated as:
            // YM_CLOCK_HZ / (256.0f * frequency) + 0.5
            write(chip, REG_ENV_FREQ,     value & 0xFF);
            write(chip, REG_ENV_FREQ + 1, (value >> 8) & 0x0F);
            break;
    }
}

void YM2149Class::setVolume(uint8_t chip, uint8_t voice, uint8_t value)
{
    if (voice > 2) return;
    value &= 0x0F;
    levelValue[chip][voice] = (levelValue[chip][voice] & 0x10) | value;
    write(chip, REG_A_LEVEL + voice, levelValue[chip][voice]);
}

void YM2149Class::setNoise(uint8_t chip, uint8_t voice, uint8_t mode)
{
    if (voice > 2) return;

    uint8_t toneMask  = 1 << voice;        // Bits 0,1,2 for tone A/B/C
    uint8_t noiseMask = 1 << (voice + 3);  // Bits 3,4,5 for noise A/B/C

    // Clear only this voice's tone+noise mask from the mixer
    mixerValue[chip] &= ~(toneMask | noiseMask);

    switch (mode)
    {
        case 1: // Tone only
            mixerValue[chip] |= noiseMask;
            break;

        case 2: // Noise only
            mixerValue[chip] |= toneMask;
            break;

        case 3: // Mute both (optional case)
            mixerValue[chip] |= (toneMask | noiseMask);
            break;

        case 0: // Both tone and noise enabled
        default:
            // leave both cleared
            break;
    }

    write(chip, REG_MIXER, mixerValue[chip]);
}

void YM2149Class::setEnv(uint8_t chip, uint8_t voice, uint8_t value)
{
    if (voice > 2) return;
    uint8_t *state = &levelValue[chip][voice];

    value &= 0b00000001;
    value <<= 4; // Bit 4 enables envelope for this voice
    *state = (*state & 0x0F) | value;
    write(chip, REG_A_LEVEL + voice, *state);
}

void YM2149Class::setEnvShape(uint8_t chip, uint8_t cont, uint8_t att, uint8_t alt, uint8_t hold)
{
    uint8_t val = ((cont & 1) << 3) | ((att & 1) << 2) | ((alt & 1) << 1) | (hold & 1);

    // Force re-trigger envelope
    write(chip, REG_ENV_SHAPE, 0);   // dummy write to retrigger
    write(chip, REG_ENV_SHAPE, val); // real value
}

void YM2149Class::mute(uint8_t chip)
{
    for (uint8_t v = 0; v < 3; ++v) {
        levelValue[chip][v] = 0;
        write(chip, REG_A_LEVEL + v, 0);
    }
    write(chip, REG_MIXER, 0b00111000); // disable tone
}
