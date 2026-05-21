using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Z25023_Mostostal_Cięcie.UI;

namespace Z25023_Mostostal_Cięcie
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var culture = new CultureInfo("pl-PL");
            this.Language = System.Windows.Markup.XmlLanguage.GetLanguage("pl-PL");

            try
            {
                // Ładowanie fizycznych parametrów gilotyny z zewnętrznego pliku JSON
                var machineConfig = MachineConfig.Load();

                //Wpisanie domyślnych wartości do pól interfejsu (z użyciem przecinka)
                txtShearMin.Text = machineConfig.ShearMin.ToString("F1", culture);
                txtShearMax.Text = machineConfig.ShearMax.ToString("F1", culture);
            }
            catch (Exception ex)
            {
                //Zabezpieczenie awaryjne, gdyby plik JSON był zablokowany lub uszkodzony
                txtShearMin.Text = "1850,7";
                txtShearMax.Text = "2650,7";
            }
        }

        // Nowy konstruktor przyjmujący parametry z zewnątrz (z projektu głównego)
        public MainWindow(double length, double pitch, double marginLeft, double marginRight, bool isSerration) : this()
        {
            var culture = new CultureInfo("pl-PL");

            // Wypełniamy formularz (wymuszając InvariantCulture dla kropek w ułamkach)
            txtLength.Text = length.ToString(culture);
            txtMarginLeft.Text = marginLeft.ToString(culture);
            txtMarginRight.Text = marginRight.ToString(culture);

            chkEnableSerration.IsChecked = isSerration;

            // Zabezpieczenie (Best Practice): Szukamy podziałki w ComboBoxie. 
            // Jeśli PLC przyśle niestandardową (np. 15.5), dodamy ją dynamicznie, by nie wywalić aplikacji.
            string pitchStr = pitch.ToString(culture);
            bool pitchFound = false;
            foreach (ComboBoxItem item in cmbPitch.Items)
            {
                if (item.Content.ToString() == pitchStr)
                {
                    cmbPitch.SelectedItem = item;
                    pitchFound = true;
                    break;
                }
            }

            if (!pitchFound)
            {
                cmbPitch.Items.Add(new ComboBoxItem { Content = pitchStr, IsSelected = true });
            }

            // Automatyczny start symulacji DOPIERO gdy UI będzie w pełni narysowane.
            // Unikamy problemów z rysowaniem po Canvasie zanim ten zostanie wyliczony przez WPF.
            RoutedEventHandler onLoaded = null;
            onLoaded = (s, e) =>
            {
                this.Loaded -= onLoaded; // WAŻNE: Odpinamy event natychmiast po pierwszym uruchomieniu!
                RunSimulationButton_Click(this, new RoutedEventArgs());
            };
            this.Loaded += onLoaded;
        }

        private void RunSimulationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 0. Bezpieczne odpięcie danych przed nową symulacją, aby zapobiec błędom WPF
                ResultsDataGrid.ItemsSource = null;
                LoopSequenceDataGrid.ItemsSource = null;
                VisualizationItemsControl.ItemsSource = null;
                VisualizationItemsControl.Items.Clear();
                CadVisualizer.DrawingCanvas.Children.Clear(); 

                // 1. Bezpieczne parsowanie parametrów wejściowych (wymuszamy kropkę jako separator dziesiętny)
                var culture = new CultureInfo("pl-PL");

                double length = double.Parse(txtLength.Text, culture);
                double marginLeft = double.Parse(txtMarginLeft.Text, culture);
                double marginRight = double.Parse(txtMarginRight.Text, culture);

                double shearMin = double.Parse(txtShearMin.Text, culture);
                double shearMax = double.Parse(txtShearMax.Text, culture);

                // Pobranie skoku (Pitch) z ComboBoxa
                var selectedPitchItem = (ComboBoxItem)cmbPitch.SelectedItem;
                double pitch = double.Parse(selectedPitchItem.Content.ToString()!, culture);

                // 2. Inicjalizacja konfiguracji (Z uwzględnieniem trybu Progresywnego / Seratacji)
                // Pobranie stanu seratacji z nowego CheckBoxa na HMI
                bool isSerrationEnabled = chkEnableSerration.IsChecked ?? false;

                // 2. Inicjalizacja konfiguracji (Z uwzględnieniem CheckBoxa)
                var machineConfig = MachineConfig.Load() with
                {
                    ShearMin = shearMin,
                    ShearMax = shearMax,
                    EnableSerration = isSerrationEnabled
                };
                var detailConfig = new DetailConfig(length, marginLeft, marginRight, pitch);

                var logic = new ProductionLogic(machineConfig);

                int optimalChunks;
                double optimalKnifePosition = logic.CalculateOptimalKnifePosition(detailConfig, out optimalChunks);

                // Wywołujemy raz dla 4 detali, aby maszyna weszła w "Stan Ustalony" (Steady State)
                var allSteps = logic.GenerateProductionSteps(detailConfig, 4, optimalKnifePosition, optimalChunks);

                int totalHoles = allSteps.Sum(s => System.Numerics.BitOperations.PopCount(s.PunchesMask));

                // ==========================================
                // DYNAMICZNE WYCIĄGANIE PĘTLI PLC (Rozpoznawanie wzorca)
                // ==========================================
                var cutIndices = new List<int>();
                for (int i = 0; i < allSteps.Count; i++)
                {
                    if (allSteps[i].IsCutActive) cutIndices.Add(i);
                }

                List<SimulationStep> steadyStateSteps = null;

                // Szukamy idealnego cyklu między 2 a 3 odcięciem płaskownika
                if (cutIndices.Count >= 3)
                {
                    int startIndex = cutIndices[1] + 1; // Start tuż po 2 cięciu
                    int endIndex = cutIndices[2];       // Koniec na 3 cięciu
                    steadyStateSteps = allSteps.GetRange(startIndex, endIndex - startIndex + 1);
                }
                else if (cutIndices.Count == 2)
                {
                    int startIndex = cutIndices[0] + 1;
                    int endIndex = cutIndices[1];
                    steadyStateSteps = allSteps.GetRange(startIndex, endIndex - startIndex + 1);
                }

                // Generowanie widoku dla Zgeneralizowanej Pętli
                if (steadyStateSteps != null)
                {
                    var loopSequence = new List<GeneralizedStep>();
                    for (int j = 0; j < steadyStateSteps.Count; j++)
                    {
                        var step = steadyStateSteps[j];
                        double stdDisp = step.StepDisplacement;
                        double startDisp = (j == 0) ? 0.0 : stdDisp;

                        loopSequence.Add(new GeneralizedStep(
                            LoopIndex: j + 1,
                            StandardDisplacement: stdDisp,
                            StartupDisplacement: startDisp,
                            IsCutActive: step.IsCutActive,
                            PunchesMask: step.PunchesMask,
                            Info: step.Info
                        ));
                    }
                    LoopSequenceDataGrid.ItemsSource = loopSequence;
                }

                // 4. Aktualizacja interfejsu użytkownika (UI)
                ResultsDataGrid.ItemsSource = allSteps;

                SummaryTextBlock.Text = $"Symulacja zakończona sukcesem. Przetworzono 4 płaskowniki. " +
                                        $"Łączna ilość uderzeń narzędzi: {totalHoles} | " +
                                        $"Marginesy: L:{marginLeft} P:{marginRight} | " +
                                        $"Zakres nożyc: {shearMin} - {shearMax} mm.";

                // Rysowanie kolejnych kroków
                foreach (var step in allSteps)
                {
                    var stepControl = new StepVisualizerControl();
                    stepControl.RenderStep(step, allSteps, machineConfig, detailConfig);

                    stepControl.BindZoom(ZoomSlider);
                    VisualizationItemsControl.Items.Add(stepControl);
                }


                // ==========================================
                // 5. RYSOWANIE ZWYMIAROWANEGO DETALU (CAD)
                // ==========================================

                // Wyliczamy czyste współrzędne dla pojedynczego detalu używając naszej zaufanej matematyki
                List<double> stdHoles = logic.CalculateHolePositions(detailConfig);
                List<double> serrHoles = new List<double>();

                if (machineConfig.EnableSerration)
                {
                    serrHoles = logic.CalculateSerrationPositions(detailConfig, stdHoles);
                }

                // Przekazujemy to do kontrolki umieszczonej w nowej zakładce
                CadVisualizer.RenderCAD(detailConfig, machineConfig, stdHoles, serrHoles);

            }
            catch (FormatException)
            {
                MessageBox.Show("Błąd formatu danych. Upewnij się, że używasz wartości liczbowych (np. z przecinkiem dziesiętnym).",
                                "Błąd Walidacji", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Wystąpił nieoczekiwany błąd podczas symulacji:\n{ex.Message}",
                                "Błąd Krytyczny", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CompareCodeButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Wywołanie nowego okna edukacyjnego z porównaniem kodu C# i Siemens SCL
            MessageBox.Show("Moduł edukacyjny (Porównanie C# vs PLC) w trakcie przygotowania.",
                            "Informacja", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RunFuzzTestButton_Click(object sender, RoutedEventArgs e)
        {
            var errors = new List<string>();
            double[] testPitches = { 11.1, 22.2, 33.3, 44.4, 55.5, 66.6, 77.7, 88.8, 99.9 };

            // Pobieramy marginesy z interfejsu (lub ustawiamy na sztywno)
            double marginL = 29.55;
            double marginR = 29.55;

            var machineConfig = MachineConfig.Load() with
            {
                ShearMin = 1850.7,
                ShearMax = 2650.7,
                EnableSerration = true
            };
            var logic = new ProductionLogic(machineConfig);

            int counter = 0;

            // Skanujemy długości detalów co 1 mm (np. od 1000 do 1200)
            for (int len = 300; len <= 2500; len++)
            {
                foreach (var pitch in testPitches)
                {
                    try
                    {
                        var detailConfig = new DetailConfig(len, marginL, marginR, pitch);

                        int optimalChunks;
                        double knifePos = logic.CalculateOptimalKnifePosition(detailConfig, out optimalChunks);
                        counter++;
                        // Symulujemy pełną produkcję 4 sztuk
                        var steps = logic.GenerateProductionSteps(detailConfig, 4, knifePos, optimalChunks);

                        // Prosta walidacja: czy w ogóle cokolwiek wygenerowano?
                        if (steps == null || steps.Count == 0)
                        {
                            throw new Exception("Zwrócono 0 kroków!");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Złapaliśmy crash! Zapisujemy sprawcę
                        errors.Add($"Długość: {len} mm, Skok: {pitch} -> {ex.Message}");
                    }
                }
            }

            // Podsumowanie testu
            if (errors.Count > 0)
            {
                // Wyświetlamy max 15 pierwszych błędów, żeby nie zablokować okienka
                string errorReport = string.Join("\n", errors.Take(15));
                if (errors.Count > 15) errorReport += $"\n...i {errors.Count - 15} więcej.";

                MessageBox.Show($"ZNALEZIONO BŁĘDY ({errors.Count}):\n\n{errorReport}",
                                "Raport ze Skanera", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show($"Skaner zakończył pracę. Brak błędów!\nPrzetestowano {counter} kombinacji.",
                                "Raport ze Skanera", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // 1. Zrywamy Data Bindingi - zwalniamy pamięć tabel
            ResultsDataGrid.ItemsSource = null;
            LoopSequenceDataGrid.ItemsSource = null;

            // 2. Brutalnie czyścimy tysiące wygenerowanych wektorów WPF
            VisualizationItemsControl.ItemsSource = null;
            VisualizationItemsControl.Items.Clear(); // Niszczy wszystkie StepVisualizerControl

            // Czyszczenie głównego rysunku CAD detalu
            if (CadVisualizer?.DrawingCanvas != null)
            {
                CadVisualizer.DrawingCanvas.Children.Clear();
            }

            // Wywołujemy standardowe zamknięcie WPF
            base.OnClosed(e);

            // 3. Agresywne wymuszenie odśmiecenia (GC)
            // W normalnych apkach tego unikamy, ale w systemach inżynieryjnych / HMI 
            // z tak ogromną ilością wektorów to jedyny sposób na "płaski" wykres użycia RAM-u.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}