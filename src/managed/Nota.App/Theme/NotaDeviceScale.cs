// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Device size (Settings → Appearance): the device cards in the Devices panel are drawn larger as
// a whole. Their layouts are fixed (700 × 260), so the Text size step leaves them alone; scaling
// the whole card row instead keeps every layout intact while its text, controls and graphs grow
// together. DeviceChainView puts its card row in a LayoutTransformControl at this factor and
// sizes the panel for it. The XL panel and device windows keep their own size.

using System;
using Nota.Application;

namespace Nota.App;

internal static class NotaDeviceScale
{
    private static int _percent = DeviceScale.Default;

    /// <summary>The size in percent (<see cref="DeviceScale.Min"/>–<see cref="DeviceScale.Max"/>).</summary>
    public static int Percent => _percent;

    /// <summary>The layout factor (1.1 at 110 %).</summary>
    public static double Factor => _percent / 100.0;

    /// <summary>Raised after the size changed, so open card rows rescale live.</summary>
    public static event Action? Changed;

    /// <summary>Wear <paramref name="percent"/> (normalised), live.</summary>
    public static void Set(int percent)
    {
        int p = DeviceScale.Normalize(percent);
        if (p == _percent) return;
        _percent = p;
        Changed?.Invoke();
    }
}
