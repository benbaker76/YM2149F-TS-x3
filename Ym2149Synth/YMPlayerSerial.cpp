#include "YMPlayerSerial.h"

// http://leonard.oxg.free.fr/ymformat.html
// http://lynn3686.com/ym3456_tidy.html
const uint8_t regMask[14] =
{
    0xFF, 0x0F,    // A period low/high
    0xFF, 0x0F,    // B period low/high
    0xFF, 0x0F,    // C period low/high
    0x1F,          // Noise period
    0x3F,          // Mixer
    0x1F,          // A volume
    0x1F,          // B volume
    0x1F,          // C volume
    0xFF, 0xFF,    // Envelope period low/high
    0x0F           // Envelope shape
};

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

void YMPlayerSerialClass::update()
{
    const int packetSize = 17;
    byte buffer[packetSize];

    size_t bytesRead = Serial.readBytes((char*)buffer, packetSize);

    if (bytesRead == packetSize)
    {
        uint8_t chip = buffer[0];

        if (chip > 2)
            return;

        Ym.setLED(chip, !Ym.getLED(chip));

        for (int i = 0; i < 14; i++)
            Ym.write(chip, i, buffer[i + 1] & regMask[i]);
    }
}
