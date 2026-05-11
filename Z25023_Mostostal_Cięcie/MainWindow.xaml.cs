using System.Globalization;
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

        }

        private void RunSimulationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Bezpieczne parsowanie parametrów wejściowych (wymuszamy kropkę jako separator dziesiętny)
                var culture = CultureInfo.InvariantCulture;

                double length = double.Parse(txtLength.Text, culture);
                double marginLeft = double.Parse(txtMarginLeft.Text, culture);
                double marginRight = double.Parse(txtMarginRight.Text, culture);

                double shearMin = double.Parse(txtShearMin.Text, culture);
                double shearMax = double.Parse(txtShearMax.Text, culture);

                // Pobranie skoku (Pitch) z ComboBoxa
                var selectedPitchItem = (ComboBoxItem)cmbPitch.SelectedItem;
                double pitch = double.Parse(selectedPitchItem.Content.ToString()!, culture);

                // 2. Inicjalizacja konfiguracji
                var machineConfig = new MachineConfig(ShearMin: shearMin, ShearMax: shearMax);
                var detailConfig = new DetailConfig(length, marginLeft, marginRight, pitch);

                var logic = new ProductionLogic(machineConfig);
                var visualizer = new SimulationVisualizer(machineConfig);

                // 3. Generowanie kroków dla 4 kolejnych płaskowników (Zgodnie z wymaganiami technicznymi)
                var allSteps = new List<SimulationStep>();
                int currentStepNumber = 1;
                int totalHoles = 0;
                // Zmienna śledząca pozycję maszyny w celu kalkulacji G91 (Przesuwu)
                double currentMachineAbsoluteDelta = 0.0;

                int optimalChunks;
                double optimalKnifePosition = logic.CalculateOptimalKnifePosition(detailConfig, out optimalChunks);

                // Zmienne do przechwycenia "złotego wzorca" (stanu ustalonego)
                List<SimulationStep> steadyStateSteps = null;

                for (int i = 0; i < 4; i++)
                {
                    double absoluteX = logic.GetAbsoluteFrontPosition(i, detailConfig);

                    // Przekazujemy zarówno pozycję noża, jak i wymuszoną liczbę podziałów
                    var detailSteps = logic.GenerateStepsForDetail(detailConfig, absoluteX, currentStepNumber, ref currentMachineAbsoluteDelta, optimalKnifePosition, optimalChunks);

                    // KRYTYCZNE: Pobieramy kroki z drugiego detalu (i == 1) jako nasz zgeneralizowany wzorzec
                    if (i == 1)
                    {
                        steadyStateSteps = new List<SimulationStep>(detailSteps);
                    }

                    allSteps.AddRange(detailSteps);
                    currentStepNumber += detailSteps.Count;
                    totalHoles += detailSteps.Sum(s => System.Numerics.BitOperations.PopCount(s.PunchesMask));
                }

                // ==========================================
                // GENEROWANIE ZAKŁADKI: ZGENERALIZOWANA PĘTLA
                // ==========================================
                if (steadyStateSteps != null)
                {
                    var loopSequence = new List<GeneralizedStep>();
                    for (int j = 0; j < steadyStateSteps.Count; j++)
                    {
                        var step = steadyStateSteps[j];

                        // Przesuw standardowy to ten z cyklu ustalonego. 
                        // Przesuw startowy wymusza 0.0 TYLKO dla pierwszego kroku w pierwszej pętli.
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

                    // Przypisanie do nowej tabeli w interfejsie
                    LoopSequenceDataGrid.ItemsSource = loopSequence;
                }

                // 4. Aktualizacja interfejsu użytkownika (UI)

                // Bindowanie danych do tabeli (DataGrid)
                ResultsDataGrid.ItemsSource = allSteps;

                // Aktualizacja paska podsumowania
                SummaryTextBlock.Text = $"Symulacja zakończona sukcesem. Przetworzono 4 płaskowniki. " +
                                        $"Łączna ilość otworów: {totalHoles} | " +
                                        $"Marginesy: L:{marginLeft} P:{marginRight} | " +
                                        $"Zakres nożyc: {shearMin} - {shearMax} mm.";

                // Czyszczenie poprzedniej wizualizacji
                VisualizationItemsControl.Items.Clear();

                // Dodawanie kolejnych kroków z symulacji
                // Dodawanie kolejnych kroków z symulacji wraz z przekazaniem pełnej historii
                foreach (var step in allSteps)
                {
                    var stepControl = new StepVisualizerControl();

                    // Zmieniona linijka: Przekazujemy allSteps jako drugi parametr!
                    stepControl.RenderStep(step, allSteps, machineConfig, detailConfig);

                    VisualizationItemsControl.Items.Add(stepControl);
                }
            }
            catch (FormatException)
            {
                MessageBox.Show("Błąd formatu danych. Upewnij się, że używasz wartości liczbowych (np. z kropką dziesiętną).",
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
    }
}