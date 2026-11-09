// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>Device size (Settings → Appearance): the device cards in the Devices panel drawn
/// larger as a whole — layout, controls and text together — in whole percent steps, on top of
/// the interface scale. The rest of the app keeps its size.</summary>
public static class DeviceScale
{
    public const int Default = 100, Min = 100, Max = 125;

    /// <summary>The stored value if it is in range, else <see cref="Default"/>.</summary>
    public static int Normalize(int percent) => percent < Min || percent > Max ? Default : percent;

    /// <summary>The layout factor for a stored value (1.1 for 110).</summary>
    public static double Factor(int percent) => Normalize(percent) / 100.0;
}
