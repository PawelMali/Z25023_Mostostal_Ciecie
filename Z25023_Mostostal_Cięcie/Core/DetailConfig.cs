using System;
using System.Collections.Generic;
using System.Text;

namespace Z25023_Mostostal_Cięcie.Core;

/// <summary>
/// Definiuje technologiczny typ cięcia/profilu matrycy.
/// </summary>
public enum CuttingType
{
    P, // Typ P - narzędzia 1, 3, 4, 5
    T  // Typ T - narzędzia 2, 6
}

/// <summary>
/// Reprezentuje parametry wejściowe pojedynczego detalu.
/// </summary>
public record DetailConfig(
        double Length,      // L
        double MarginLeft,  // ML
        double MarginRight, // MP
        double HolePitch,    // P
        CuttingType Type    // Profil cięcia (P lub T)
    );
