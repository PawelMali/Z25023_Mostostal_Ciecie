using System;
using System.Collections.Generic;
using System.Text;

namespace Z25023_Mostostal_Cięcie.Core;

/// <summary>
/// Reprezentuje stałe i konfigurowalne parametry fizyczne prasy.
/// </summary>
public record MachineConfig(
    double ShearMin = 1850.7,
    double ShearMax = 2650.7,
    double BladeWidth = 6.0,   // Nowa zmienna: Grubość noża (rzaz)
    double Pitch = 33.3,
    double MachineCenterIndex = 15.5,
    int MaxPunches = 32,
    double PunchWidth = 1.5, // NOWA ZMIENNA: Grubość / szerokość stempla w mm

    // NOWE PARAMETRY:
    bool EnableSerration = true,  // Włącznik trybu progresywnego
    double SerrationWidth = 5.0   // Szerokość (średnica) stempla seratacji
);
