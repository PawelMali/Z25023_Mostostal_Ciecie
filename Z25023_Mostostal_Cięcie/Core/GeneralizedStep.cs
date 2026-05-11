using System;
using System.Collections.Generic;
using System.Text;

namespace Z25023_Mostostal_Cięcie.Core;

/// <summary>
/// Reprezentuje pojedynczy krok w zgeneralizowanej pętli produkcyjnej (Podprogram).
/// </summary>
public record GeneralizedStep(
    int LoopIndex,
    double StandardDisplacement,
    double StartupDisplacement,
    bool IsCutActive,
    uint PunchesMask,
    string Info
);
