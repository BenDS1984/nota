// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Transpose implementation.

#include "MidiTranspose.h"
#include <algorithm>
#include <cmath>

namespace nota {

MidiTranspose::MidiTranspose() {
    p_[POctave].store(0.0f);
    p_[PSemitones].store(0.0f);
    p_[PFollow].store(1.0f);
    reset();
}

const char* MidiTranspose::paramName(int32_t i) const {
    switch (i) {
    case POctave: return "Octave";
    case PSemitones: return "Semitones";
    case PFollow: return "Follow Held";
    default: return "";
    }
}

int MidiTranspose::shift() const {
    const int oct = std::clamp((int)std::lround(p_[POctave].load(std::memory_order_relaxed)), -kOctMax, kOctMax);
    const int semi = std::clamp((int)std::lround(p_[PSemitones].load(std::memory_order_relaxed)), -kSemiMax, kSemiMax);
    return oct * 12 + semi;
}

std::shared_ptr<MidiDevice> MidiTranspose::clone() const {
    auto c = std::make_shared<MidiTranspose>();
    for (int i = 0; i < kNumParams; ++i) c->p_[i].store(p_[i].load(std::memory_order_relaxed));
    c->setBypassed(bypassed());
    return c;
}

void MidiTranspose::reset() {
    std::fill(std::begin(outFor_), std::end(outFor_), kFree);
    std::fill(std::begin(outRef_), std::end(outRef_), (uint8_t)0);
    std::fill(std::begin(velFor_), std::end(velFor_), 0.0f);
}

void MidiTranspose::process(
    const MidiEv* in, int nIn, MidiEv* out, int& nOut, int maxOut, int32_t, double, double, bool) {
    nOut = 0;
    auto emit = [&](const MidiEv& e, int pitch) {
        if (nOut >= maxOut) return;
        MidiEv o = e;
        o.pitch = pitch;
        out[nOut++] = o;
    };
    // A held key lets go at the pitch it sounds as; a shared pitch only when its last key goes.
    auto release = [&](const MidiEv& e) {
        const int p = outFor_[e.pitch];
        if (p == kFree) { // never seen (held before the device was added): pass it on
            emit(e, e.pitch);
            return;
        }
        outFor_[e.pitch] = kFree;
        if (p == kSwallowed) return;
        if (outRef_[p] > 0 && --outRef_[p] == 0) emit(e, p);
    };

    // Bypassed: notes pass unchanged, but keys held from before still let go where they sound.
    const int by = bypassed() ? 0 : shift();
    auto press = [&](const MidiEv& e) {
        if (outFor_[e.pitch] != kFree) release({e.off, false, e.pitch, 0.0f}); // a retriggered key lets go first
        const int p = e.pitch + by;
        velFor_[e.pitch] = e.vel;
        lastIn_.store(e.pitch, std::memory_order_relaxed);
        if (p < 0 || p > 127) { // outside MIDI: silent, and so is its note-off
            outFor_[e.pitch] = kSwallowed;
            lastOut_.store(-1, std::memory_order_relaxed);
            return;
        }
        outFor_[e.pitch] = (int16_t)p;
        if (outRef_[p]++ > 0) emit({e.off, false, p, 0.0f}, p); // the pitch already sounds: restart it
        emit(e, p);
        lastOut_.store(p, std::memory_order_relaxed);
    };

    // Follow held notes: the amount changed since the last block, so every key still down that
    // now belongs somewhere else lets go where it sounds and starts again at the new pitch. All
    // let go first, then all start, so two keys trading places never cut each other off.
    if (by != lastBy_ && p_[PFollow].load(std::memory_order_relaxed) >= 0.5f) {
        bool moved[128] = {};
        int nMoved = 0;
        for (int k = 0; k < 128; ++k) {
            const int16_t cur = outFor_[k];
            if (cur == kFree) continue;
            const int p = k + by;
            const int16_t want = (p < 0 || p > 127) ? kSwallowed : (int16_t)p;
            if (want == cur) continue;
            release({0, false, k, 0.0f});
            moved[k] = true;
            ++nMoved;
        }
        if (nMoved > 0)
            for (int k = 0; k < 128; ++k)
                if (moved[k]) press({0, true, k, velFor_[k]});
    }
    lastBy_ = by;

    for (int i = 0; i < nIn; ++i) {
        const MidiEv& e = in[i];
        if (e.pitch < 0 || e.pitch > 127) continue;
        if (!e.on) {
            release(e);
            continue;
        }
        press(e);
    }
}

} // namespace nota
