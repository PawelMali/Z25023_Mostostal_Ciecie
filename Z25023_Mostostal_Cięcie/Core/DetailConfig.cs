using System;
using System.Collections.Generic;
using System.Text;

namespace Z25023_Mostostal_Cięcie.Core;

/// <summary>
/// Reprezentuje parametry wejściowe pojedynczego detalu.
/// </summary>
public record DetailConfig(
        double Length,      // L
        double MarginLeft,  // ML
        double MarginRight, // MP
        double HolePitch    // P
    );
