using ScottPlot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using Z25023_Mostostal_Cięcie.Core;

namespace Z25023_Mostostal_Cięcie.UI;

public class SimulationVisualizer
{
    private readonly Core.MachineConfig _machine;

    public SimulationVisualizer(Core.MachineConfig machineConfig)
    {
        _machine = machineConfig;
    }

    /// <summary>
    /// Rysuje schemat "krok po kroku" na dostarczonej kontrolce ScottPlot.Plot.
    /// Odwzorowuje stany maszyny schodząc kaskadowo w dół wzdłuż osi Y.
    /// </summary>
    public void DrawSimulationWaterfall(Plot plot, List<Core.SimulationStep> steps, double detailLength)
    {
        // Czyszczenie poprzedniego wykresu
        plot.Clear();

        // Rysujemy wirtualną strefę nożyc (Shear) w tle dla referencji wzrokowej
        // Używamy Add.VerticalSpan, aby zaznaczyć strefę cięcia przez wszystkie kroki
        var shearZone = plot.Add.VerticalSpan(_machine.ShearMin, _machine.ShearMax);
        shearZone.FillColor = Colors.LightCoral.WithAlpha(0.2);

        // Label strefy nożyc
        var shearLabel = plot.Add.Text("STREFA CIĘCIA NOŻYC", _machine.ShearMin + 50, 2);
        shearLabel.FontSize = 14;
        shearLabel.Color = Colors.Red;

        // Rysowanie poszczególnych kroków z góry na dół
        foreach (var step in steps)
        {
            // Y będzie ujemne, aby kolejne kroki rysowały się coraz niżej (kaskada)
            double yPosition = -step.StepNumber * 2.0;

            // 1. Rysowanie płaskownika w danym kroku
            // Materiał zaczyna się w (FrontPosition - detailLength) i kończy w (FrontPosition)
            double barStartX = step.FrontPosition - detailLength;
            double barEndX = step.FrontPosition;

            var bar = plot.Add.Rectangle(barStartX, barEndX, yPosition - 0.5, yPosition + 0.5);
            bar.FillColor = Colors.LightGray;
            bar.LineColor = Colors.DarkGray;

            // 2. Dekodowanie maski bitowej (DWORD) i rysowanie wybitych otworów
            for (int i = 0; i < _machine.MaxPunches; i++)
            {
                // Sprawdzenie, czy dany stempel był aktywny w tym kroku
                if ((step.PunchesMask & (1u << i)) != 0)
                {
                    // Współrzędna absolutna otworu: Przesuw(Delta) + fizyczna pozycja stempla
                    double punchX = step.Delta + (i * _machine.Pitch);

                    // Rysujemy otwór jako czerwony punkt na płaskowniku
                    var marker = plot.Add.Marker(punchX, yPosition, MarkerShape.FilledCircle, size: 8);
                    marker.Color = Colors.Red;
                }
            }

            // 3. Wizualizacja cięcia w locie (SYNC CUT / STOP)
            if (step.IsCutActive || step.Info.Contains("CUT"))
            {
                // W momencie cięcia, czoło materiału znajduje się pod nożycami
                var cutMarker = plot.Add.Marker(step.FrontPosition, yPosition, MarkerShape.Cross, size: 12);
                cutMarker.Color = Colors.Blue;
            }

            // 4. Etykiety współrzędnych i opis kroku ułatwiające weryfikację algorytmów
            string infoText = $"[KROK {step.StepNumber}] Δ:{step.Delta:F1} | X:{step.FrontPosition:F1} | {step.Info}";
            var text = plot.Add.Text(infoText, barEndX + 20, yPosition);
            text.FontSize = 10;
            text.Color = Colors.Black;
            text.Alignment = Alignment.MiddleLeft;
        }

        // 5. Konfiguracja i skalowanie osi
        plot.Axes.Title.Label.Text = "Symulacja Kaskadowa Wykrawania (Krok po Kroku)";
        plot.Axes.Bottom.Label.Text = "Pozycja Absolutna (mm)";
        plot.Axes.Left.Label.Text = "Oś Czasu (Kolejne Kroki)";

        // Ukrywamy liczby na osi Y, ponieważ kroki reprezentowane są przez tekst
        plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual();

        // Automatyczne dopasowanie kamery do obwiedni wszystkich narysowanych elementów
        plot.Axes.AutoScale();
    }
}
