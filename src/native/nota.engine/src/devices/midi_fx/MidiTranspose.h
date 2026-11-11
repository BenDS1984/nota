// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Transpose — a MIDI effect (midiKind 7). Moves every note by whole octaves (−4..+4) and
// semitones (−12..+12) before the instrument. A held note always lets go at the pitch it
// sounds as, so changing the amount (by hand or automation) while keys are down never hangs a
// note. With Follow held notes on, a change moves the keys still down: each lets go where it
// sounded and starts again at the new pitch (same velocity), at the start of the next block;
// off, they keep sounding where they began. A note pushed outside 0..127 is swallowed, and so
// is its note-off (Follow brings it back once the amount is in range again). Two held keys that
// land on the same pitch sound once and let go when the last of them is released. The last
// note (in → out) is published for the editor.

#pragma once

#include "devices/MidiDevice.h"
#include <atomic>
#include <cstdint>
#include <memory>

namespace nota {

class MidiTranspose final : public MidiDevice {
  public:
    enum Param {
        POctave = 0, // whole octaves, −4..+4
        PSemitones,  // semitones, −12..+12
        PFollow,     // 0/1: a change of amount moves the notes still held (default on)
        kNumParams   // APPEND ONLY: the order is the saved layout
    };
    static constexpr int kOctMax = 4, kSemiMax = 12;

    MidiTranspose();

    int32_t midiKind() const override { return 7; }
    const char* displayName() const override { return "Nota Transpose"; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t i) const override {
        return i == POctave ? (float)-kOctMax : i == PSemitones ? (float)-kSemiMax : 0.0f;
    }
    float paramMax(int32_t i) const override {
        return i == POctave ? (float)kOctMax : i == PSemitones ? (float)kSemiMax : 1.0f;
    }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed);
    }

    // Live telemetry: the last note-on's input and output pitch (-1 = none yet).
    int32_t midiLastIn() const override { return lastIn_.load(std::memory_order_relaxed); }
    int32_t midiLastOut() const override { return lastOut_.load(std::memory_order_relaxed); }

    std::shared_ptr<MidiDevice> clone() const override;

    // Transport stop / loop wrap: the engine silences the instrument, so forget what is held.
    void reset() override;

    void process(const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t frames, double beatStart,
        double spb, bool playing) override;

    /// The total shift in semitones for the current params (octaves × 12 + semitones).
    int shift() const;

  private:
    static constexpr int16_t kFree = -1, kSwallowed = -2;
    int16_t outFor_[128]; // input key → the pitch it sounds as (kFree = not held, kSwallowed = out of range)
    uint8_t outRef_[128]; // output pitch → how many held keys share it
    float velFor_[128];   // input key → its note-on velocity (to start it again when it follows)
    int lastBy_ = 0;      // the shift applied in the previous block
    std::atomic<int32_t> lastIn_{-1}, lastOut_{-1};
    std::atomic<float> p_[kNumParams];
};

} // namespace nota
