// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Transpose (MIDI effect kind 7): the param layout, the shift (octaves × 12 + semitones),
// a note pushed outside 0..127 being swallowed, and — the one that matters on stage — a note
// held while the amount changes still letting go (no hanging voice). `--transpose` runs these alone.

using System;
using System.Collections.Generic;
using Nota.Application;
using Nota.Infrastructure;

internal static class TransposeTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.SetTimeSignature(4, 4);
        int t = e.AddInstrumentTrack();
        int m = e.AddMidiEffect(t, 7);
        yield return (m == 0 && e.MidiEffectName(t, 0) == "Nota Transpose" && e.MidiEffectKind(t, 0) == 7, "add Nota Transpose");
        int pc = e.MidiEffectParamCount(t, 0);
        yield return (pc == 2 && e.MidiEffectParamName(t, 0, 0) == "Octave" && e.MidiEffectParamName(t, 0, 1) == "Semitones",
            $"two params, Octave then Semitones (got {pc})");
        yield return (e.MidiEffectParamMin(t, 0, 0) == -4f && e.MidiEffectParamMax(t, 0, 0) == 4f
                      && e.MidiEffectParamMin(t, 0, 1) == -12f && e.MidiEffectParamMax(t, 0, 1) == 12f,
            "Octave spans −4..+4, Semitones −12..+12");
        yield return (e.MidiEffectGetParam(t, 0, 0) == 0f && e.MidiEffectGetParam(t, 0, 1) == 0f, "defaults: no shift");

        e.AddMidiClip(t, 0.0, 4.0);
        var buf = new float[4096 * 2];
        (int In, int Out) Play(int pitch, int oct, int semi)
        {
            e.StopTransport(); e.RenderOffline(buf, 256);
            e.MidiEffectSetParam(t, 0, 0, oct); e.MidiEffectSetParam(t, 0, 1, semi);
            e.SetClipNotes(t, 0, new[] { new NotaNote(pitch, 0.0, 0.5, 0.9f) });
            e.Seek(0); e.Play(); e.RenderOffline(buf, 4096); e.StopTransport();
            return (e.MidiEffectLastIn(t, 0), e.MidiEffectLastOut(t, 0));
        }
        var a = Play(60, 0, 7);
        yield return (a == (60, 67), $"C4 + 7 semitones comes out as G4 (in {a.In} → out {a.Out})");
        var b = Play(60, -1, 0);
        yield return (b == (60, 48), $"C4 − 1 octave comes out as C3 (in {b.In} → out {b.Out})");
        var c = Play(62, 1, -2);
        yield return (c == (62, 72), $"octave and semitones add up: D4 + 12 − 2 = C5 (in {c.In} → out {c.Out})");
        var d = Play(120, 4, 0);
        yield return (d == (120, -1), $"a note pushed above 127 is swallowed (in {d.In} → out {d.Out})");

        // Held across a change: the note-off must reach the pitch the note-on went to. A hung voice
        // keeps sounding at its sustain level, so the output long after the note-off tells.
        static double Peak(float[] b) { double p = 0; foreach (var s in b) p = Math.Max(p, Math.Abs(s)); return p; }
        // First prove the check can see a hung note: a held note on this instrument still sounds after
        // the same ~3.7 s (if the sustain were 0, a hung voice would fade on its own and prove nothing).
        e.StopTransport(); e.RenderOffline(buf, 256);
        e.MidiEffectSetParam(t, 0, 0, 0); e.MidiEffectSetParam(t, 0, 1, 0);
        e.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 4.0, 0.9f) });   // the whole clip: 2 s, still held at the loop end
        e.Seek(0); e.Play();
        e.RenderOffline(buf, 4096);
        for (int i = 0; i < 18; i++) e.RenderOffline(buf, 4096);              // ~1.7 s in, the key still down
        double held = Peak(buf);
        e.StopTransport();
        yield return (held > 1e-3, $"the test instrument keeps a held note sounding (peak {held:0.000}), so a hung voice would show");

        e.StopTransport(); e.RenderOffline(buf, 256);
        e.MidiEffectSetParam(t, 0, 0, 0); e.MidiEffectSetParam(t, 0, 1, 7);
        e.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 1.0, 0.9f) });   // half a second at 120 bpm, then nothing
        e.Seek(0); e.Play();
        e.RenderOffline(buf, 4096);                                          // the note is on, sounding as G4
        double during = Peak(buf);
        e.MidiEffectSetParam(t, 0, 1, 5);                                   // the amount changes while it is held
        for (int i = 0; i < 40; i++) e.RenderOffline(buf, 4096);              // ~3.7 s: well past the note-off and any release
        double after = Peak(buf);
        e.StopTransport();
        yield return (during > 1e-3 && after < 1e-4, $"a note held while the shift changes still lets go (peak {during:0.000} → {after:0.00000})");
    }
}
