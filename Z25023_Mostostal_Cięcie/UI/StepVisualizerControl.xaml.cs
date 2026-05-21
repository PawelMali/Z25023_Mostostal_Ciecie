using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Z25023_Mostostal_Cięcie.Core;

namespace Z25023_Mostostal_Cięcie.UI;

public partial class StepVisualizerControl : UserControl
{
    // Przesunięcie płótna, aby widzieć maszynę od -2000mm 
    private const double OffsetX = 700;
    private const double AxisY = 160.0;

    public StepVisualizerControl()
    {
        InitializeComponent();
    }

    private double GetCanvasX(double machineX)
    {
        return machineX + OffsetX;
    }

    public void RenderStep(SimulationStep currentStep, IEnumerable<SimulationStep> history, MachineConfig machine, DetailConfig detail)
    {
        DrawingCanvas.Children.Clear();

        // ==========================================
        // 0. OBLICZENIE MARGINESU LEWEGO I ZLICZANIE STEMPLI
        // ==========================================
        // Obliczamy fizyczny odstęp uwzględniający grubość narzędzia
        double availableLength = detail.Length - detail.MarginLeft - detail.MarginRight;

        int intervals = availableLength > 0 && detail.HolePitch > 0 ? (int)Math.Floor(availableLength / detail.HolePitch) : 0;

        // FAKTYCZNY Margines Lewy = Długość detalu - baza prawa - dystans otworów - cała grubość stempla
        double actualMarginRight = detail.Length - detail.MarginRight - (intervals * detail.HolePitch);

        int activePunchesCount = System.Numerics.BitOperations.PopCount(currentStep.PunchesMask);

        // ZIELONY NAGŁÓWEK
        RunTitle.Text = $"KROK {currentStep.StepNumber}: Delta (Przesuw) = +{currentStep.StepDisplacement:F2} mm | Pozycja noża X = {currentStep.CutTargetX:F2} mm | Margines Lewy = {detail.MarginLeft:F2} mm | Margines Prawy = {actualMarginRight:F2} mm";

        // NIEBIESKI NAGŁÓWEK
        RunDetails.Text = $"INFO: {currentStep.Info} | Liczba aktywnych stempli: {activePunchesCount} | STEMPLE (Mask): {currentStep.PunchesMask}";

        RunTitle.Foreground = currentStep.IsCutActive ? Brushes.DarkGreen : Brushes.Green;

        // ==========================================
        // 1. OŚ GŁÓWNA I ZNACZNIKI (POWIĘKSZONA CZCIONKA)
        // ==========================================
        DrawingCanvas.Children.Add(new Line { X1 = 0, Y1 = AxisY, X2 = DrawingCanvas.Width, Y2 = AxisY, Stroke = Brushes.Black, StrokeThickness = 1 });

        for (int x = -2000; x <= 4500; x += 500)
        {
            double tickCanvasX = GetCanvasX(x);
            DrawingCanvas.Children.Add(new Line { X1 = tickCanvasX, Y1 = AxisY - 5, X2 = tickCanvasX, Y2 = AxisY + 5, Stroke = Brushes.Black });

            // Czcionka powiększona do 16 dla osi głównej
            var label = new TextBlock { Text = x.ToString(), FontSize = 20, Foreground = Brushes.Black, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(label, tickCanvasX - 20);
            Canvas.SetTop(label, AxisY + 10);
            DrawingCanvas.Children.Add(label);
        }

        // ==========================================
        // 2. STREFA NOŻYC
        // ==========================================
        double shearCanvasStart = GetCanvasX(machine.ShearMin);
        double shearCanvasEnd = GetCanvasX(machine.ShearMax);

        var shearZone = new Rectangle
        {
            Width = shearCanvasEnd - shearCanvasStart,
            Height = 15,
            Fill = new SolidColorBrush(Color.FromArgb(100, 255, 100, 100))
        };
        Canvas.SetLeft(shearZone, shearCanvasStart);
        Canvas.SetTop(shearZone, AxisY + 10);
        DrawingCanvas.Children.Add(shearZone);

        var shearText = new TextBlock { Text = "STREFA CIĘCIA", Foreground = Brushes.Firebrick, FontSize = 16, FontWeight = FontWeights.Bold };
        Canvas.SetLeft(shearText, shearCanvasStart + 10);
        Canvas.SetTop(shearText, AxisY + 28);
        DrawingCanvas.Children.Add(shearText);

        // ==========================================
        // 3. RYSOWANIE STEMPLI Z NUMERACJĄ (Podział na 2 strefy)
        // ==========================================
        for (int i = 0; i < machine.MaxPunches; i++)
        {
            bool isActive = (currentStep.PunchesMask & (1u << i)) != 0;
            bool isSerrationZone = machine.EnableSerration && i < machine.SerrationMaxPunches;

            double punchMachineX = (i - machine.MachineCenterIndex) * machine.Pitch;
            double punchCanvasX = GetCanvasX(punchMachineX);
            double punchWidth = 2;//isSerrationZone ? machine.SerrationWidth : machine.PunchWidth;

            // Narzędzie seratacji rysujemy na zielono, standardowe na czerwono
            Brush activeBrush = isSerrationZone ? Brushes.LimeGreen : Brushes.Red;
            Brush activeStroke = isSerrationZone ? Brushes.DarkGreen : Brushes.DarkRed;

            var punchShape = new Rectangle
            {
                Width = punchWidth,
                Height = 40,
                RadiusX = 2,
                RadiusY = 2,
                Fill = isActive ? activeBrush : Brushes.WhiteSmoke,
                Stroke = isActive ? activeStroke : Brushes.LightGray,
                StrokeThickness = 1
            };
            Canvas.SetLeft(punchShape, punchCanvasX - (punchWidth / 2.0));
            Canvas.SetTop(punchShape, AxisY - 110);
            DrawingCanvas.Children.Add(punchShape);

            // Numeracja z dopasowanym kolorem
            var punchLabel = new TextBlock
            {
                Text = i.ToString(),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Width = 20,
                TextAlignment = TextAlignment.Center,
                Foreground = isActive ? activeBrush : Brushes.Gray
            };
            Canvas.SetLeft(punchLabel, punchCanvasX - 10);
            Canvas.SetTop(punchLabel, AxisY - 138);
            DrawingCanvas.Children.Add(punchLabel);
        }

        // ==========================================
        // 4. PŁASKOWNIK (Czysta reprezentacja, kończy się dokładnie na Delcie)
        // ==========================================
        double materialFrontMachineX = currentStep.Delta;

        // Pokazujemy tylko wygenerowaną historię (4 detale w tył), bez sztucznego przedłużania w prawo
        double totalMaterialVisible = 4 * (detail.Length + machine.BladeWidth);
        double materialBackMachineX = materialFrontMachineX - totalMaterialVisible;

        double frontCanvasX = GetCanvasX(materialFrontMachineX);
        double backCanvasX = GetCanvasX(materialBackMachineX);

        var materialBar = new Rectangle
        {
            Width = frontCanvasX - backCanvasX,
            Height = 60,
            Fill = new SolidColorBrush(Color.FromRgb(135, 206, 235)),
            Stroke = Brushes.Black,
            StrokeThickness = 1
        };
        Canvas.SetLeft(materialBar, backCanvasX);
        Canvas.SetTop(materialBar, AxisY - 60);
        DrawingCanvas.Children.Add(materialBar);

        // ==========================================
        // 4A. SZCZELINY I RZAZ (Zaczynamy od k=0, czyli odcięcia czoła)
        // ==========================================
        for (int k = 0; k <= 4; k++)
        {
            // K=0 to szczelina miedzy odpadem a czołem D1, K=1 to szczelina miedzy D1 a D2
            double gapRightAbs = k * (detail.Length + machine.BladeWidth) - machine.BladeWidth;
            double gapLeftAbs = k * (detail.Length + machine.BladeWidth);

            double gapRightMachineX = materialFrontMachineX - gapRightAbs;
            double gapLeftMachineX = materialFrontMachineX - gapLeftAbs;

            double cutRightCanvas = GetCanvasX(gapRightMachineX);
            double cutLeftCanvas = GetCanvasX(gapLeftMachineX);

            if (cutRightCanvas >= backCanvasX && cutLeftCanvas <= frontCanvasX)
            {
                var kerfRect = new Rectangle { Width = cutRightCanvas - cutLeftCanvas, Height = 60, Fill = Brushes.DarkSlateGray, Opacity = 0.5 };
                Canvas.SetLeft(kerfRect, cutLeftCanvas);
                Canvas.SetTop(kerfRect, AxisY - 60);
                DrawingCanvas.Children.Add(kerfRect);

                // Etykieta pozycji rzazu na osi X
                // 1. Obliczamy idealny środek szczeliny w układzie współrzędnych maszyny
                double gapCenterMachineX = (gapRightMachineX + gapLeftMachineX) / 2.0;
                double gapCenterCanvasX = GetCanvasX(gapCenterMachineX);

                var gapLabel = new TextBlock
                {
                    Text = gapCenterMachineX.ToString("F2"),
                    FontSize = 20,
                    Foreground = Brushes.DimGray,
                    FontWeight = FontWeights.Bold
                };

                // 2. Wymuszamy przeliczenie rozmiaru tekstu, aby precyzyjnie wyśrodkować go pod szczeliną
                gapLabel.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                Canvas.SetLeft(gapLabel, gapCenterCanvasX - (gapLabel.DesiredSize.Width / 2.0));

                // 3. Pozycjonujemy tekst na AxisY + 40 (główne etykiety osi są na +10, więc ten tekst będzie pod nimi)
                Canvas.SetTop(gapLabel, AxisY + 40);

                DrawingCanvas.Children.Add(gapLabel);
                // --------------------------------------------------
            }
        }

        // ==========================================
        // 5. HISTORIA OTWORÓW I SERATACJI NA MATERIALE
        // ==========================================
        foreach (var pastStep in history)
        {
            if (pastStep.StepNumber > currentStep.StepNumber) continue;

            for (int i = 0; i < machine.MaxPunches; i++)
            {
                if ((pastStep.PunchesMask & (1u << i)) != 0)
                {
                    double pastPunchMachineX = (i - machine.MachineCenterIndex) * machine.Pitch;
                    double currentHoleMachineX = pastPunchMachineX + (currentStep.Delta - pastStep.Delta);
                    double holeCanvasX = GetCanvasX(currentHoleMachineX);

                    if (holeCanvasX >= backCanvasX && holeCanvasX <= frontCanvasX)
                    {
                        bool isSerrationZone = machine.EnableSerration && i < machine.SerrationMaxPunches;

                        if (isSerrationZone)
                        {
                            // ZMIANA: Rysowanie seratacji jako zielonej kreski na 1/3 wysokości brzegu blachy
                            // Płaskownik ma 40mm wys. (od AxisY-50 do AxisY-10).
                            // 1/3 wysokości blachy to około 13.33mm.
                            DrawingCanvas.Children.Add(new Line
                            {
                                X1 = holeCanvasX,
                                Y1 = AxisY - 50, // Początek na górnej krawędzi blachy
                                X2 = holeCanvasX,
                                Y2 = AxisY - 36.67, // Koniec w 1/3 wysokości blachy
                                Stroke = Brushes.DarkGreen,
                                StrokeThickness = 1.5,
                                Opacity = 0.9
                            });
                        }
                        else
                        {
                            // Standardowy rzaz na środku blachy (nie zmieniony, czerwony, pełna wysokość)
                            DrawingCanvas.Children.Add(new Line { X1 = holeCanvasX, Y1 = AxisY - 50, X2 = holeCanvasX, Y2 = AxisY - 10, Stroke = Brushes.Red, StrokeThickness = 1.5 });
                        }
                    }
                }
            }
        }

        // ==========================================
        // 6. IDEALNY NÓŻ I CIĘCIE (POWIĘKSZONA CZCIONKA)
        // ==========================================
        double knifeCenterMachineX = currentStep.CutTargetX;
        double knifeCenterCanvasX = GetCanvasX(knifeCenterMachineX);

        DrawingCanvas.Children.Add(new Line
        {
            X1 = knifeCenterCanvasX,
            Y1 = AxisY - 80,
            X2 = knifeCenterCanvasX,
            Y2 = AxisY + 30,
            Stroke = Brushes.Magenta,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 4 }
        });

        if (currentStep.IsCutActive)
        {
            var activeKnifeRect = new Rectangle
            {
                Width = machine.BladeWidth,
                Height = 35,
                Fill = Brushes.Red,
                Opacity = 0.7
            };
            Canvas.SetLeft(activeKnifeRect, GetCanvasX(knifeCenterMachineX - machine.BladeWidth / 2.0));
            Canvas.SetTop(activeKnifeRect, AxisY - 15);
            DrawingCanvas.Children.Add(activeKnifeRect);

            // Powiększony, wyraźny tekst dla cięcia (FontSize = 20)
            var exactCutText = new TextBlock
            {
                Text = $"CIĘCIE AKTYWNE (X: {knifeCenterMachineX:F2})",
                Foreground = Brushes.Red,
                FontSize = 20,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(exactCutText, knifeCenterCanvasX - 75);
            Canvas.SetTop(exactCutText, AxisY - 105);
            DrawingCanvas.Children.Add(exactCutText);
        }
        else
        {
            // Powiększony, czytelniejszy tekst celownika (FontSize = 20)
            var standbyText = new TextBlock
            {
                Text = $"CEL NOŻA (X: {knifeCenterMachineX:F2})",
                Foreground = Brushes.Gray,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold
            };
            Canvas.SetLeft(standbyText, knifeCenterCanvasX - 60);
            Canvas.SetTop(standbyText, AxisY - 105);
            DrawingCanvas.Children.Add(standbyText);
        }
    }

    /// <summary>
    /// Podpina właściwości skalowania płótna (Zoom) pod suwak z okna głównego.
    /// </summary>
    public void BindZoom(Slider zoomSlider)
    {
        var binding = new Binding("Value") { Source = zoomSlider };
        BindingOperations.SetBinding(CanvasScale, ScaleTransform.ScaleXProperty, binding);
        BindingOperations.SetBinding(CanvasScale, ScaleTransform.ScaleYProperty, binding);
    }
}