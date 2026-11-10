// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Transpose editor (MIDI effect kind 7), one S card (260 × 260):
// the total shift large ("+7 st") with its interval name ("perfect fifth up"), a three-octave
// keyboard with the last note played outlined (in) and where it went filled (out), and two
// knobs — Octave (−4…+4) and Semitones (−12…+12), whole steps only — and a Follow held notes
// switch (a change moves the keys still down, on by default). The keyboard shows real
// notes from the device (telemetry), not a demo.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

/// <summary>Nota Transpose's params in musical units — mirrors MidiTranspose.h.</summary>
internal static class TransposeModel
{
    internal const int POctave = 0, PSemitones = 1, PFollow = 2;
    internal const int OctMax = 4, SemiMax = 12;

    internal static int Octave(Func<int, float> g) => Math.Clamp((int)Math.Round(g(POctave)), -OctMax, OctMax);
    internal static int Semitones(Func<int, float> g) => Math.Clamp((int)Math.Round(g(PSemitones)), -SemiMax, SemiMax);
    internal static int Total(Func<int, float> g) => Octave(g) * 12 + Semitones(g);

    internal static string Signed(int v) => v > 0 ? $"+{v}" : v < 0 ? $"−{-v}" : "0";

    /// <summary>"+7 st", "−12 st", "0 st".</summary>
    internal static string TotalText(int total) => Loc.F("device.transpose.semitones_value", Signed(total));

    /// <summary>The interval the shift spans, with its direction: "perfect fifth up", "1 octave + major
    /// third down", "no shift".</summary>
    internal static string IntervalText(int total)
    {
        if (total == 0) return Loc.T("device.transpose.no_shift");
        int a = Math.Abs(total), oct = a / 12, rest = a % 12;
        string name = rest switch
        {
            0 => "",
            1 => Loc.T("device.transpose.minor_second"),
            2 => Loc.T("device.transpose.major_second"),
            3 => Loc.T("device.transpose.minor_third"),
            4 => Loc.T("device.transpose.major_third"),
            5 => Loc.T("device.transpose.perfect_fourth"),
            6 => Loc.T("device.transpose.tritone"),
            7 => Loc.T("device.transpose.perfect_fifth"),
            8 => Loc.T("device.transpose.minor_sixth"),
            9 => Loc.T("device.transpose.major_sixth"),
            10 => Loc.T("device.transpose.minor_seventh"),
            _ => Loc.T("device.transpose.major_seventh"),
        };
        string span = oct == 0 ? name
            : rest == 0 ? Loc.Plural("device.transpose.octaves", oct)
            : Loc.F("device.transpose.octaves_and", Loc.Plural("device.transpose.octaves", oct), name);
        return total > 0 ? Loc.F("device.transpose.up", span) : Loc.F("device.transpose.down", span);
    }
}

internal sealed class TransposeMidiBody : IMidiDeviceBody
{
    private const int POctave = TransposeModel.POctave, PSemitones = TransposeModel.PSemitones;

    private static readonly IBrush Ground = NotaPalette.SurfaceCard;
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Cap = NotaPalette.TextTertiary;
    private static readonly IBrush Muted = NotaPalette.TextMuted;

    public double Width => 260;
    public bool FullBleed => true;

    public Control? HeaderAccessory(DeviceCardContext ctx, int index) => MidiCardHeader.LearnButton(ctx);

    private static TextBlock Mono(string t, IBrush c, double fs) => new()
    { Text = t, FontSize = fs, FontFamily = NotaFonts.UiFamily, Foreground = c, VerticalAlignment = VerticalAlignment.Center };

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine; int track = ctx.TrackId, mi = index;
        if (engine.MidiEffectParamCount(track, mi) <= PSemitones) return new TextBlock { Text = "Nota Transpose", Margin = new Thickness(8) };
        float G(int p) => engine.MidiEffectGetParam(track, mi, p);
        void S(int p, double v) => engine.MidiEffectSetParam(track, mi, p, (float)v);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.MidiDeviceParam, mi, p, "");
        bool Bypassed() => engine.MidiEffectBypassed(track, mi);

        var readouts = new System.Collections.Generic.List<Action>();
        void Refresh() { foreach (var r in readouts) r(); }

        // ---- the shift, large ----
        var total = Mono("", Txt, 22); total.FontWeight = FontWeight.Medium; total.HorizontalAlignment = HorizontalAlignment.Center;
        var interval = Mono("", Sub, 9); interval.HorizontalAlignment = HorizontalAlignment.Center;
        interval.TextTrimming = TextTrimming.CharacterEllipsis;
        readouts.Add(() =>
        {
            int t = TransposeModel.Total(G);
            total.Text = TransposeModel.TotalText(t);
            interval.Text = TransposeModel.IntervalText(t);
            total.Foreground = Bypassed() ? Cap : Txt;
        });
        var head = new StackPanel { Spacing = 1, Margin = new Thickness(0, 6, 0, 4), Children = { total, interval } };

        // ---- the last note: in (outlined) → out (filled) ----
        var keys = new ChordKeysViz { Labels = true, Height = 46 };
        ToolTip.SetTip(keys, Loc.T("device.transpose.keyboard_tip"));
        var lastIO = Mono("", Sub, 8); lastIO.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() =>
        {
            int pin = engine.MidiEffectLastIn(track, mi), pout = engine.MidiEffectLastOut(track, mi);
            if (pin < 0) { keys.Set(48, Array.Empty<(int, bool)>(), -1); lastIO.Text = Loc.T("device.transpose.play_a_note"); return; }
            // Three octaves from the C at or below the lower of the two; if they are further apart, follow the output.
            int lo = pout >= 0 ? Math.Min(pin, pout) : pin, hi = pout >= 0 ? Math.Max(pin, pout) : pin;
            int start = Math.Clamp(lo / 12 * 12, 0, 96);
            if (hi - start > 35 && pout >= 0) start = Math.Clamp(pout / 12 * 12 - 12, 0, 96);
            keys.Set(start, pout >= 0 ? new[] { (pout, true) } : Array.Empty<(int, bool)>(), pin);
            lastIO.Text = pout >= 0 ? $"{DeviceCardKit.NoteName(pin)} → {DeviceCardKit.NoteName(pout)}"
                                    : Loc.F("device.transpose.out_of_range", DeviceCardKit.NoteName(pin));
        });

        // ---- the knobs: whole steps ----
        Control KnobCell(string label, int param, int max)
        {
            double Norm() => (Math.Clamp(Math.Round(G(param)), -max, max) + max) / (2.0 * max);
            var knob = new Knob(Norm(), 1.0) { Accent = true, Bipolar = true, Width = 38, Height = 38, HorizontalAlignment = HorizontalAlignment.Center, Default = 0.5 };
            knob.ValueChanged += n => { S(param, Math.Round(n * 2 * max) - max); Refresh(); };
            knob.GestureBegin += () => Begin(param);
            knob.GestureEnd += () => End(param);
            knob.Format = n => TransposeModel.Signed((int)(Math.Round(n * 2 * max) - max));
            MidiLearn.Bind(knob, MidiTarget.MidiDeviceParam(track, mi, param), label);
            var lbl = new TextBlock { Text = NotaText.Label(label), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium, Foreground = Cap, HorizontalAlignment = HorizontalAlignment.Center };
            var v = Mono("", Sub, 8); v.HorizontalAlignment = HorizontalAlignment.Center;
            ValueEntry.Attach(knob, label, v, () => TransposeModel.Signed((int)Math.Round(G(param))));
            readouts.Add(() =>
            {
                double n = Norm();
                if (!knob.Dragging && Math.Abs(knob.Value - n) > 1e-6) knob.Value = n;
                v.Text = TransposeModel.Signed(Math.Clamp((int)Math.Round(G(param)), -max, max));
            });
            return new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Children = { knob, lbl, v } };
        }
        var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 6, 0, 2) };
        var oct = KnobCell(Loc.T("device.transpose.octave"), POctave, TransposeModel.OctMax);
        var semi = KnobCell(Loc.T("device.transpose.semitones"), PSemitones, TransposeModel.SemiMax);
        Grid.SetColumn(semi, 1);
        knobs.Children.Add(oct); knobs.Children.Add(semi);

        // ---- Follow held notes: a switch + sentence-case label ----
        Control Flag(int p, string label, string tip)
        {
            var sw = new SwitchTrack { VerticalAlignment = VerticalAlignment.Center };
            var tb = new TextBlock { Text = label, FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
            var row = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { sw, tb } } };
            row.PointerPressed += (_, ev) =>
            {
                if (!ev.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                Begin(p); S(p, G(p) >= 0.5f ? 0 : 1); End(p); Refresh(); ev.Handled = true;
            };
            ToolTip.SetTip(row, tip);
            MidiLearn.Bind(row, MidiTarget.MidiDeviceParam(track, mi, p), label);
            readouts.Add(() => { bool on = G(p) >= 0.5f; sw.IsOn = on; tb.Foreground = on ? Txt : Muted; });
            return row;
        }
        Control? follow = engine.MidiEffectParamCount(track, mi) > TransposeModel.PFollow
            ? Flag(TransposeModel.PFollow, Loc.T("device.transpose.follow_held"), Loc.T("device.transpose.follow_held_tip"))
            : null;

        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(knobs, Dock.Bottom);
        DockPanel.SetDock(lastIO, Dock.Bottom);
        var inner = new DockPanel { Children = { head } };
        if (follow != null) { DockPanel.SetDock(follow, Dock.Bottom); inner.Children.Add(follow); }
        inner.Children.Add(knobs); inner.Children.Add(lastIO); inner.Children.Add(keys);
        keys.VerticalAlignment = VerticalAlignment.Center;
        var island = new RuleSection { Padding = new Thickness(8, 4, 8, 4), Child = inner };

        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return new Border { Background = Ground, Padding = new Thickness(5), Child = island };
    }
}
