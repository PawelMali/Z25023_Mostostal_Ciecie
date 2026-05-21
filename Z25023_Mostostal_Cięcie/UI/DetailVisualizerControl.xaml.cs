using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Z25023_Mostostal_Cięcie.Core;

namespace Z25023_Mostostal_Cięcie.UI;

public partial class DetailVisualizerControl : UserControl
{
    private const double OffsetX = 100.0; // Lewy margines na płótnie
    private const double BarY = 200.0;    // Pozycja Y górnej krawędzi płaskownika
    private const double BarHeight = 80.0;// Wizualna wysokość płaskownika
    private const double HoleHeight = 40.0; // Wizualna wysokość wycięcia prostokątnego

    public DetailVisualizerControl()
    {
        InitializeComponent();
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CanvasScale != null)
        {
            CanvasScale.ScaleX = e.NewValue;
            CanvasScale.ScaleY = e.NewValue;
        }
    }

    public void RenderCAD(DetailConfig detail, MachineConfig machine, List<double> standardHoles, List<double> serrationHoles)
    {
        DrawingCanvas.Children.Clear();
        DrawingCanvas.Width = detail.Length + 200; // Dopasowanie płótna do długości detalu

        // ==========================================
        // 0. TRANSFORMACJA WSPÓŁRZĘDNYCH (Lustrzane Odbicie)
        // System maszynowy C# traktuje prawe czoło jako X=0. Ekran traktuje lewy brzeg jako X=0.
        // Odwracamy współrzędne (L - x), aby lewy margines był po lewej, a prawy po prawej.
        // ==========================================
        List<double> visualStdHoles = standardHoles.Select(x => detail.Length - x).OrderBy(x => x).ToList();
        List<double> visualSerrHoles = serrationHoles.Select(x => detail.Length - x).OrderBy(x => x).ToList();

        // ==========================================
        // 1. RYSOWANIE MATERIAŁU (Styl CAD: Szare tło, czarny obrys)
        // ==========================================
        var materialBar = new Rectangle
        {
            Width = detail.Length,
            Height = BarHeight,
            Fill = new SolidColorBrush(Color.FromRgb(200, 200, 200)), // Techniczny jasny szary
            Stroke = Brushes.Black,
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(materialBar, OffsetX);
        Canvas.SetTop(materialBar, BarY);
        DrawingCanvas.Children.Add(materialBar);

        // ==========================================
        // 2. OTORY STANDARDOWE I OSIE SYMETRII
        // ==========================================
        foreach (double hx in visualStdHoles)
        {
            double holeCanvasX = OffsetX + hx;

            // Wycięcie prostokątne (szczelina)
            var punchCut = new Rectangle
            {
                Width = machine.PunchWidth,
                Height = HoleHeight,
                Fill = Brushes.White, // Tło (efekt wycięcia w blasze)
                Stroke = Brushes.Black,
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(punchCut, holeCanvasX - (machine.PunchWidth / 2.0));
            Canvas.SetTop(punchCut, BarY + (BarHeight / 2.0) - (HoleHeight / 2.0));
            DrawingCanvas.Children.Add(punchCut);

            // Oś symetrii (Linia punktowo-kreskowa CAD)
            var centerLine = new Line
            {
                X1 = holeCanvasX,
                Y1 = BarY - 20,
                X2 = holeCanvasX,
                Y2 = BarY + BarHeight + 20,
                Stroke = Brushes.Black,
                StrokeThickness = 0.75,
                StrokeDashArray = new DoubleCollection { 15, 5, 3, 5 } // Długa kreska, przerwa, kropka, przerwa
            };
            DrawingCanvas.Children.Add(centerLine);
        }

        // ==========================================
        // 3. SERATACJA I ZNACZNIKI KRZYŻOWE (+)
        // ==========================================
        if (machine.EnableSerration)
        {
            foreach (double sx in visualSerrHoles)
            {
                double serrCanvasX = OffsetX + sx;

                // Półokrągłe wycięcie
                var serrCut = new Ellipse
                {
                    Width = machine.SerrationWidth,
                    Height = machine.SerrationWidth,
                    Fill = Brushes.White,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1.5
                };
                Canvas.SetLeft(serrCut, serrCanvasX - (machine.SerrationWidth / 2.0));
                Canvas.SetTop(serrCut, BarY - (machine.SerrationWidth / 2.0));
                DrawingCanvas.Children.Add(serrCut);

                // Biały prostokąt maskujący górną połowę elipsy (aby linia krawędzi detalu była czysta)
                var mask = new Rectangle { Width = machine.SerrationWidth + 2, Height = machine.SerrationWidth / 2.0 + 1, Fill = Brushes.White };
                Canvas.SetLeft(mask, serrCanvasX - (machine.SerrationWidth / 2.0) - 1);
                Canvas.SetTop(mask, BarY - (machine.SerrationWidth / 2.0) - 1);
                DrawingCanvas.Children.Add(mask);

                // Znacznik środka (Krzyżyk + w stylu CAD)
                double crossSize = 8;
                DrawingCanvas.Children.Add(new Line { X1 = serrCanvasX, Y1 = BarY - crossSize, X2 = serrCanvasX, Y2 = BarY + crossSize, Stroke = Brushes.Black, StrokeThickness = 0.75 });
                DrawingCanvas.Children.Add(new Line { X1 = serrCanvasX - crossSize, Y1 = BarY, X2 = serrCanvasX + crossSize, Y2 = BarY, Stroke = Brushes.Black, StrokeThickness = 0.75 });
            }
        }

        // ==========================================
        // 4. WYMIAROWANIE (CAD DRAFTING) - ROZBUDOWANE PIĘTROWANIE
        // ==========================================

        // PIĘTRO 4: Całkowita długość detalu (Najbardziej oddalony wymiar dolny)
        DrawDimensionLine(OffsetX, OffsetX + detail.Length, BarY + BarHeight + 85, BarY, $"Lpn = {detail.Length:F1}", true);

        // DOLNE WYMIARY (Otwory standardowe)
        if (visualStdHoles.Count > 0)
        {
            double firstHoleX = OffsetX + visualStdHoles.First();
            double lastHoleX = OffsetX + visualStdHoles.Last();

            double actualMarginLeft = visualStdHoles.First();
            double actualMarginRight = detail.Length - visualStdHoles.Last();

            // PIĘTRO 1 (Dolne): Marginesy (bezpośrednio przy płaskowniku)
            DrawDimensionLine(OffsetX, firstHoleX, BarY + BarHeight + 25, BarY + BarHeight, $"{actualMarginLeft:F2}");
            DrawDimensionLine(lastHoleX, OffsetX + detail.Length, BarY + BarHeight + 25, BarY + BarHeight, $"{actualMarginRight:F2}");

            int stdGaps = visualStdHoles.Count - 1;
            if (stdGaps > 0)
            {
                // Przesuwamy etykietę podziałki na 3 szczelinę (lub max dostępną), by odsunąć ją od marginesu
                int pIndex1 = 0;
                int pIndex2 = 1;
                if (visualStdHoles.Count > 3) { pIndex1 = 2; pIndex2 = 3; }
                else if (visualStdHoles.Count > 2) { pIndex1 = 1; pIndex2 = 2; }

                double pStartX = OffsetX + visualStdHoles[pIndex1];
                double pEndX = OffsetX + visualStdHoles[pIndex2];

                // PIĘTRO 2 (Dolne): Pojedyncza podziałka standardowa na osobnej wysokości
                DrawDimensionLine(pStartX, pEndX, BarY + BarHeight + 45, BarY + BarHeight, $"p={detail.HolePitch:F1}");

                // PIĘTRO 3 (Dolne): Skumulowany skok otworów standardowych (n x Pitch)
                DrawDimensionLine(firstHoleX, lastHoleX, BarY + BarHeight + 65, BarY + BarHeight, $"{stdGaps} x {detail.HolePitch:F1} = {stdGaps * detail.HolePitch:F1}");
            }
        }

        // GÓRNE WYMIARY (Seratacja)
        if (machine.EnableSerration && visualSerrHoles.Count > 0)
        {
            double firstSerrX = OffsetX + visualSerrHoles.First();
            double lastSerrX = OffsetX + visualSerrHoles.Last();

            double actualSerrMarginLeft = visualSerrHoles.First();
            double actualSerrMarginRight = detail.Length - visualSerrHoles.Last();

            // PIĘTRO 1 (Górne): Marginesy seratacji (przy samej krawędzi)
            DrawDimensionLine(OffsetX, firstSerrX, BarY - 25, BarY, $"{actualSerrMarginLeft:F2}");
            DrawDimensionLine(lastSerrX, OffsetX + detail.Length, BarY - 25, BarY, $"{actualSerrMarginRight:F2}");

            int serrGaps = visualSerrHoles.Count - 1;
            if (serrGaps > 0)
            {
                // Przesuwamy etykietę podziałki seratacji na 4 szczelinę
                int sIndex1 = 0;
                int sIndex2 = 1;
                if (visualSerrHoles.Count > 4) { sIndex1 = 3; sIndex2 = 4; }
                else if (visualSerrHoles.Count > 3) { sIndex1 = 2; sIndex2 = 3; }
                else if (visualSerrHoles.Count > 2) { sIndex1 = 1; sIndex2 = 2; }

                double pSerrStartX = OffsetX + visualSerrHoles[sIndex1];
                double pSerrEndX = OffsetX + visualSerrHoles[sIndex2];

                // PIĘTRO 2 (Górne): Skok pojedynczej seratacji na nowej, niezależnej wysokości
                DrawDimensionLine(pSerrStartX, pSerrEndX, BarY - 45, BarY, $"p={machine.SerrationPitch:F1}");

                // PIĘTRO 3 (Górne): Skumulowany skok seratacji (n x 11.1)
                DrawDimensionLine(firstSerrX, lastSerrX, BarY - 65, BarY, $"{serrGaps} x {machine.SerrationPitch:F1} = {serrGaps * machine.SerrationPitch:F1}");
            }
        }
    }

    /// <summary>
    /// Rysuje precyzyjną linię wymiarową ze strzałkami w stylu AutoCAD.
    /// Posiada inteligentny algorytm wyrzucający groty na zewnątrz dla wąskich marginesów.
    /// </summary>
    private void DrawDimensionLine(double startX, double endX, double yLevel, double extensionTargetY, string text, bool isBold = false)
    {
        // Linie wyniesienia (pomocnicze cienkie kreski pionowe)
        double extOffset = (yLevel > extensionTargetY) ? 5 : -5;
        DrawingCanvas.Children.Add(new Line { X1 = startX, Y1 = extensionTargetY + extOffset, X2 = startX, Y2 = yLevel + extOffset * 2, Stroke = Brushes.Black, StrokeThickness = 0.5 });
        DrawingCanvas.Children.Add(new Line { X1 = endX, Y1 = extensionTargetY + extOffset, X2 = endX, Y2 = yLevel + extOffset * 2, Stroke = Brushes.Black, StrokeThickness = 0.5 });

        double arrowLen = 9;
        double arrowHeight = 2.5;

        // Zabezpieczenie dla małych wymiarów (strzałki na zewnątrz, groty do wewnątrz)
        bool isSmallSpace = (endX - startX) < 45;

        if (isSmallSpace)
        {
            // Linia główna wyciągnięta na zewnątrz
            DrawingCanvas.Children.Add(new Line { X1 = startX - 25, Y1 = yLevel, X2 = endX + 25, Y2 = yLevel, Stroke = Brushes.Black, StrokeThickness = 0.75 });

            // Lewa strzałka zewnętrzna (grot w prawo)
            DrawingCanvas.Children.Add(new Polygon
            {
                Fill = Brushes.Black,
                Points = new PointCollection { new Point(startX, yLevel), new Point(startX - arrowLen, yLevel - arrowHeight), new Point(startX - arrowLen, yLevel + arrowHeight) }
            });

            // Prawa strzałka zewnętrzna (grot w lewo)
            DrawingCanvas.Children.Add(new Polygon
            {
                Fill = Brushes.Black,
                Points = new PointCollection { new Point(endX, yLevel), new Point(endX + arrowLen, yLevel - arrowHeight), new Point(endX + arrowLen, yLevel + arrowHeight) }
            });
        }
        else
        {
            // Normalna linia pomiędzy liniami wyniesienia
            DrawingCanvas.Children.Add(new Line { X1 = startX, Y1 = yLevel, X2 = endX, Y2 = yLevel, Stroke = Brushes.Black, StrokeThickness = 0.75 });

            // Lewa strzałka wewnętrzna (grot w lewo)
            DrawingCanvas.Children.Add(new Polygon
            {
                Fill = Brushes.Black,
                Points = new PointCollection { new Point(startX, yLevel), new Point(startX + arrowLen, yLevel - arrowHeight), new Point(startX + arrowLen, yLevel + arrowHeight) }
            });

            // Prawa strzałka wewnętrzna (grot w prawo)
            DrawingCanvas.Children.Add(new Polygon
            {
                Fill = Brushes.Black,
                Points = new PointCollection { new Point(endX, yLevel), new Point(endX - arrowLen, yLevel - arrowHeight), new Point(endX - arrowLen, yLevel + arrowHeight) }
            });
        }

        // Etykieta wymiaru
        var label = new TextBlock
        {
            Text = text,
            Background = Brushes.White,
            Padding = new Thickness(4, 0, 4, 0),
            Foreground = Brushes.Black,
            FontStyle = FontStyles.Italic, // Lekka pochylona czcionka inżynierska CAD
            FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
            FontSize = 13
        };

        label.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
        double textWidth = label.DesiredSize.Width;

        Canvas.SetLeft(label, startX + ((endX - startX) / 2.0) - (textWidth / 2.0));
        Canvas.SetTop(label, yLevel - 18);
        DrawingCanvas.Children.Add(label);
    }
}