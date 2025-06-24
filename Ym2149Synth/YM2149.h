#pragma once
#include <Arduino.h>

class YM2149Class {

public:
    static constexpr uint8_t DATA_PINS_D = 0b11111100; // D2-D7
    static constexpr uint8_t DATA_PINS_B = 0b00000011; // D8-D9
    static constexpr uint8_t BC1_BIT = _BV(PB6);    // D10 → PB6
    static constexpr uint8_t BDIR_BIT = _BV(PF5);   // D20 → PF5
    static constexpr uint8_t SEL_A_BIT = _BV(PF4);  // A3 → PF4
    static constexpr uint8_t SEL_B_BIT = _BV(PF6);  // A1 → PF6
    static constexpr uint8_t SEL_C_BIT = _BV(PF7);  // A0 → PF7
    static constexpr uint8_t ENABLE_BIT = _BV(PF5); // A2 → PF5

    /* -------- physical pins -------- */
    static constexpr uint8_t PIN_BC1   = 10;
    static constexpr uint8_t PIN_BDIR  = 20;
    static constexpr uint8_t PIN_SEL_A = A3;
    static constexpr uint8_t PIN_SEL_B = A1;
    static constexpr uint8_t PIN_SEL_C = A0;
    static constexpr uint8_t PIN_ENABLE= A2;
    static constexpr uint8_t CHIP_LED[3] = {15, 14, 16};

    /* -------- YM registers -------- */
    static const uint8_t REG_A_FREQ = 0x00;
    static const uint8_t REG_B_FREQ = 0x02;
    static const uint8_t REG_C_FREQ = 0x04;
    static const uint8_t REG_NOISE_FREQ = 0x06;
    static const uint8_t REG_MIXER = 0x07;
    static const uint8_t REG_A_LEVEL = 0x08;
    static const uint8_t REG_B_LEVEL = 0x09;
    static const uint8_t REG_C_LEVEL = 0x0A;
    static const uint8_t REG_ENV_FREQ = 0x0B;
    static const uint8_t REG_ENV_SHAPE = 0x0D;
    static const uint8_t REG_DATAPORT_A = 0x0E;
    static const uint8_t REG_DATAPORT_B = 0x0F;

    static constexpr float YM_CLOCK_HZ = 500000.0f;

    static constexpr uint8_t LED_ON = LOW;
    static constexpr uint8_t LED_OFF = HIGH;

    static constexpr uint8_t YM_0 = 0;
    static constexpr uint8_t YM_1 = 1;
    static constexpr uint8_t YM_2 = 2;

    void begin();
    void selectYM(uint8_t chip);
    void busWrite(uint8_t value);
    void writeFast(uint8_t address, uint8_t value);
    void write(uint8_t chip, uint8_t address, uint8_t value);
    void setPin(uint8_t chip, uint8_t pin, bool value);
    uint8_t getPin(uint8_t chip, uint8_t pin);

    void setPort(uint8_t chip, bool port, uint8_t value);
    uint8_t getPort(uint8_t chip, bool port);
    void setPortIO(uint8_t chip, bool portA, bool portB);

    void setLED(uint8_t chip, bool state);
    bool getLED(uint8_t chip);

	void setNote(uint8_t chip, uint8_t synth, float value);
	void setFreq(uint8_t chip, uint8_t voice, uint32_t freqHz);
    void setTone(uint8_t chip, uint8_t voice, uint16_t value);
    void setVolume(uint8_t chip, uint8_t voice, uint8_t value);
    void setNoise(uint8_t chip, uint8_t voice, uint8_t value);
    void setEnv(uint8_t chip, uint8_t voice, uint8_t value);
    void setEnvShape(uint8_t chip, uint8_t cont, uint8_t att, uint8_t alt, uint8_t hold);
    void mute(uint8_t chip);

    volatile static uint8_t currentChip;

private:
    uint8_t levelValue[3][3] = {{0}};
    uint8_t portAValue[3] = {0};
    uint8_t portBValue[3] = {0};
    uint8_t mixerValue[3] = {0b00111000, 0b00111000, 0b00111000};
    bool ledState[3] = {false, false, false};
};

typedef YM2149Class YM2149;
