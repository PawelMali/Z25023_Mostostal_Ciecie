using System;
using System.Collections.Generic;
using System.Text;

namespace Z25023_Mostostal_Cięcie.Core;

/// <summary>
/// Reprezentuje pojedynczy krok symulacji do wyświetlenia w tabeli.
/// Typ 'record' zapewnia niemutowalność i ułatwia bindowanie w WPF.
/// </summary>
public record struct SimulationStep(
    int StepNumber,
    double Delta,             // Absolutna pozycja maszyny (G90)
    double StepDisplacement,  // NOWE: Rzeczywisty przesuw materiału w danym kroku (G91)
    double FrontPosition,
    uint PunchesMask,         // DWORD dla PLC
    bool IsCutActive,         // ZMIENIONE: Wskazuje, czy gilotyna wykonuje cięcie
    double CutTargetX,        // NOWE: Współrzędna osi noża (środek ostrza)
    string Info
);
