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

#define MAX_PATCHES 16

#ifndef SYNTHPATCHSTORAGE_h
#define SYNTHPATCHSTORAGE_h

#include "Arduino.h"
#include "EEPROM.h"
#include "SynthVoice.h"

class SynthPatchStorageClass {
  public:
    void begin();
    void init();
    void load(SynthVoice * synth, int preset);
    void getPatch(uint8_t * buffer, int patch);
    void save();
    void save(uint8_t patch) {
        selectedPatch = patch;
        flush();
        save();
    };
    void flush();
    void recall();
    void setValue(uint8_t address,uint8_t value);
    void writeValue(uint8_t address,uint8_t value);
    void setChipIndex(uint8_t index) { chipIndex = index; }

    static const uint8_t numberPatches = MAX_PATCHES;
    static const uint8_t patchSize = 11;

  private:
    uint8_t selectedPatch;
    uint8_t chipIndex = 0;
    uint8_t patchTemp[patchSize];

    static uint8_t bank[MAX_PATCHES][patchSize];
};

typedef SynthPatchStorageClass SynthPatchStorage;

#endif
