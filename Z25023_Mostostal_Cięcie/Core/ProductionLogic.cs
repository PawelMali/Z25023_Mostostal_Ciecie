using System;
using System.Collections.Generic;
using System.Text;
using System.Linq; // Wymagane dla metody ToList(), First(), Last()

namespace Z25023_Mostostal_Cięcie.Core;

/// <summary>
/// Serce matematyczne symulatora prasy wykrawającej (Tłocznika Postępowego).
/// Odpowiada za transformacje układów współrzędnych, grupowanie narzędzi (Load Balancing),
/// obliczanie przeplotów (Interleaving) oraz synchronizację cięcia w locie (Flying Shear).
/// </summary>
public class ProductionLogic
{
    private readonly MachineConfig _machine;

    public ProductionLogic(MachineConfig machineConfig)
    {
        _machine = machineConfig;
    }

    /// <summary>
    /// TRANSFORMACJA WSPÓŁRZĘDNYCH: Oblicza absolutną pozycję czoła detalu w nieskończonym strumieniu materiału.
    /// 
    /// Matematyka:
    /// Materiał to ciągła taśma. Każdy wycięty detal zostawia w materiale "rzaz" (szczelinę) o grubości noża (W_noża).
    /// Wzór na pozycję czoła n-tego detalu: X_front(n) = n * (L_detalu + W_noża)
    /// </summary>
    /// <param name="detailIndex">Indeks detalu w partii (0 dla D1, 1 dla D2, itd.)</param>
    public double GetAbsoluteFrontPosition(int detailIndex, DetailConfig detail)
    {
        return detailIndex * (detail.Length + _machine.BladeWidth);
    }

    /// <summary>
    /// GENERATOR MASKI BITOWEJ (PLC): Tłumaczy tablicę dziesiętnych indeksów narzędzi na format 32-bitowy.
    /// Standard stosowany w przemyśle do szybkiej komunikacji ze sterownikiem PLC (np. Siemens SCL).
    /// </summary>
    public uint GeneratePunchMask(IEnumerable<int> activePunches)
    {
        uint mask = 0;
        foreach (var punchIndex in activePunches)
        {
            if (punchIndex >= 0 && punchIndex < _machine.MaxPunches)
            {
                mask |= (1u << punchIndex); // Operacja bitowa OR z przesunięciem (Shift Left)
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(punchIndex), $"Indeks stempla {punchIndex} poza zakresem maszyny!");
            }
        }
        return mask;
    }

    /// <summary>
    /// BALANSER OBCIĄŻENIA (LOAD BALANCER): Dzieli całkowitą liczbę otworów na pakiety uderzeń.
    /// Zapobiega powstawaniu pakietów resztkowych (np. uderzeń 1-stemplowych), dążąc do równomiernego zużycia narzędzi.
    /// 
    /// Matematyka:
    /// N_chunks = Wymuszona Ilość LUB Ceil(TotalHoles / MaxPunches)
    /// Rozmiar bazowy: BaseSize = Floor(TotalHoles / N_chunks)
    /// Reszta z dzielenia: Remainder = TotalHoles MOD N_chunks (rozdzielana po +1 do pierwszych paczek).
    /// </summary>
    public List<int> CalculateBalancedChunks(int totalHoles, int availablePunches, int forcedChunks = 0)
    {
        if (totalHoles <= 0) return new List<int>();
        // przykład 23 / 10 = 2.3 → Ceil → 3 pakiety
        // Jeśli 'forcedChunks' jest podane i większe od zera, użyj go zamiast wyliczonej liczby pakietów.
        int numberOfChunks = forcedChunks > 0 ? forcedChunks : (int)Math.Ceiling((double)totalHoles / availablePunches);
        if (numberOfChunks <= 0) numberOfChunks = 1;
        if (numberOfChunks > totalHoles) numberOfChunks = totalHoles;

        //Ile otworów w każdym pakiecie: 23 otwory, 3 pakiety → BaseSize = 7, Remainder = 2 → Pakiety: [8, 8, 7]
        int baseSize = totalHoles / numberOfChunks;
        int remainder = totalHoles % numberOfChunks;
        var chunks = new List<int>(numberOfChunks);
        for (int i = 0; i < numberOfChunks; i++) chunks.Add(baseSize + (i < remainder ? 1 : 0));
        return chunks;
    }

    /// <summary>
    /// WYZNACZANIE WSPÓŁRZĘDNYCH OTWORÓW: Oblicza idealne rozmieszczenie otworów na detalu (względem jego własnego zera).
    /// 
    /// Matematyka:
    /// Aby zachować fizyczny margines krawędzi (nie przeciąć obwodu detalu), środek stempla (TCP) 
    /// musi być odsunięty o promień narzędzia: r = PunchWidth / 2.0.
    /// 
    /// Bezpieczny Margines = M + r.
    /// Przestrzeń Robocza (L_dost) = L_detalu - (M_prawy + r) - (M_lewy + r).
    /// Ilość Otworów (N) = Floor(L_dost / P_otworu) + 1.
    /// Pozycja i-tego otworów: X(i) = (M_prawy + r) + (i * P_otworu).
    /// </summary>
    public List<double> CalculateHolePositions(DetailConfig detail)
    {
        var positions = new List<double>();

        double centerMarginRight = detail.MarginRight;
        double centerMarginLeft = detail.MarginLeft;
        double availableLength = detail.Length - centerMarginRight - centerMarginLeft;

        if (availableLength < 0) return positions;
        if (detail.HolePitch <= 0) throw new ArgumentException("Skok otworów musi być większy od zera.");

        int numberOfIntervals = (int)Math.Floor(availableLength / detail.HolePitch);
        int numberOfHoles = numberOfIntervals + 1;

        //for (int i = 0; i < numberOfHoles; i++)
        //{
        //    positions.Add(centerMarginRight + (i * detail.HolePitch));
        //}

        // NOWA LOGIKA: Baza odwrócona. Sztywny jest lewy margines (na końcu detalu względem kierunku X maszyny).
        // Najbardziej lewy otwór znajdzie się na pozycji: (Length - MarginLeft).
        // Najpierw wyliczamy, o ile musi przesunąć się prawy margines, by przejąć całą resztę (remainder).
        double actualMarginRight = detail.Length - centerMarginLeft - (numberOfIntervals * detail.HolePitch);

        for (int i = 0; i < numberOfHoles; i++)
        {
            // Startujemy od wyliczonego, powiększonego prawego marginesu
            positions.Add(actualMarginRight + (i * detail.HolePitch));
        }

        return positions;
    }

    /// <summary>
    /// SILNIK GŁÓWNY (UNIFIED PIPELINE): Synchronizuje uderzenia w przestrzeni.
    /// Rozwiązuje problem Tłocznika Postępowego: Seratacja (narzędzia lewe) musi zgrać się
    /// z Otworowaniem (narzędzia prawe) na ciągłym strumieniu materiału.
    /// </summary>
    public List<SimulationStep> GenerateProductionSteps(DetailConfig detail, int detailCount, double optimalKnifeX, int forcedChunks)
    {
        var allGlobalPasses = new List<(PunchPass Pass, double AbsoluteFrontX)>();

        // 6A: Generowanie "surowych" map uderzeń (Passes) dla całej zleconej partii.
        for (int d = 0; d < detailCount; d++)
        {
            double absoluteDetailFrontX = GetAbsoluteFrontPosition(d, detail);
            List<double> standardHoles = CalculateHolePositions(detail);

            if (_machine.EnableSerration)
            {
                // STREFA 1: SERATACJA (Indeksy dynamiczne pobrane z pliku konfiguracyjnego)
                List<double> serrationHoles = CalculateSerrationPositions(detail, standardHoles);
                int effSerration = GetEffectiveMaxPunches(_machine.SerrationPitch, _machine.SerrationMaxPunches);

                List<int> serrChunkSizes = CalculateBalancedChunks(serrationHoles.Count, effSerration, 0);
                int sHoleIndex = 0;
                foreach (int chunkSize in serrChunkSizes)
                {
                    // Elastyczny zakres: od 0 do (SerrationMaxPunches - 1)
                    var passes = CalculatePassesForChunk(serrationHoles.GetRange(sHoleIndex, chunkSize), _machine.SerrationPitch, 0, _machine.SerrationMaxPunches - 1);
                    foreach (var p in passes) allGlobalPasses.Add((p, absoluteDetailFrontX));
                    sHoleIndex += chunkSize;
                }

                // STREFA 2: STANDARD
                int availableStandardPunches = _machine.MaxPunches - _machine.SerrationMaxPunches;
                int effStandard = GetEffectiveMaxPunches(detail.HolePitch, availableStandardPunches);

                List<int> stdChunkSizes = CalculateBalancedChunks(standardHoles.Count, effStandard, forcedChunks);
                int stdHoleIndex = 0;
                foreach (int chunkSize in stdChunkSizes)
                {
                    // Elastyczny zakres: od SerrationMaxPunches do (MaxPunches - 1)
                    var passes = CalculatePassesForChunk(standardHoles.GetRange(stdHoleIndex, chunkSize), detail.HolePitch, _machine.SerrationMaxPunches, _machine.MaxPunches - 1);
                    foreach (var p in passes) allGlobalPasses.Add((p, absoluteDetailFrontX));
                    stdHoleIndex += chunkSize;
                }
            }
            else
            {
                // TRYB KLASYCZNY: Cała maszyna (Indeksy 0 - 31) działa nad standardowymi otworami.
                int effStandard = GetEffectiveMaxPunches(detail.HolePitch, _machine.MaxPunches);
                List<int> stdChunkSizes = CalculateBalancedChunks(standardHoles.Count, effStandard, forcedChunks);
                int stdHoleIndex = 0;
                foreach (int chunkSize in stdChunkSizes)
                {
                    var passes = CalculatePassesForChunk(standardHoles.GetRange(stdHoleIndex, chunkSize), detail.HolePitch, 0, _machine.MaxPunches - 1);
                    foreach (var p in passes) allGlobalPasses.Add((p, absoluteDetailFrontX));
                    stdHoleIndex += chunkSize;
                }
            }
        }

        // 6B: KRYTYCZNE SORTOWANIE GLOBALNE (Zabezpieczenie przed cofaniem maszyny)
        // Matematyka: Grupowanie po absolutnej pozycji maszyny: Math.Round(Δ_maszyny, 2).
        // Rozwiązuje problem wyprzedzania się kroków z różnych stref i detali.
        var groupedPasses = allGlobalPasses
            .GroupBy(item => Math.Round(CalculateMachineDelta(item.AbsoluteFrontX, item.Pass.ReferenceHoleX, item.Pass.ReferencePunchIndex), 2))
            .OrderBy(g => g.Key)
            .ToList();

        var steps = new List<SimulationStep>();
        double lastAbsoluteDelta = 0;
        int currentStep = 1;
        double detailTotalLength = detail.Length + _machine.BladeWidth;

        // 6C: Kompresja zgrupowanych uderzeń w konkretne ramki (Steps) dla sterownika PLC
        foreach (var group in groupedPasses)
        {
            uint combinedMask = 0;
            bool isMicrostep = true;
            double currentMaterialDelta = group.Key;

            // Jeśli uderzenie seratacji i standardowe zgrało się idealnie w tym samym mikrokroku, złącz maski bitowe
            foreach (var item in group)
            {
                combinedMask |= GeneratePunchMask(item.Pass.ActivePunchIndices);
                if (!item.Pass.IsMicrostep) isMicrostep = false;
            }

            double stepDisplacement = currentMaterialDelta - lastAbsoluteDelta;
            if (currentStep == 1) stepDisplacement = 0.0; // Rozruch maszyny
            lastAbsoluteDelta = currentMaterialDelta;

            // Weryfikacja synchronizacji noża (cięcie z odrzutem/rzazem)
            bool isCutActive = false;
            if (!isMicrostep)
            {
                double absoluteMaterialUnderKnife = currentMaterialDelta - optimalKnifeX;
                double shifted = absoluteMaterialUnderKnife + (_machine.BladeWidth / 2.0);

                // Modulo cięcia: najbliższa wielokrotność długości całkowitej detalu.
                double multiple = Math.Round(shifted / detailTotalLength);
                double nearestKerfShifted = multiple * detailTotalLength;

                if (Math.Abs(shifted - nearestKerfShifted) < 0.1) isCutActive = true;
            }

            string info = isCutActive ? "PUNCH & SYNC CUT" : (isMicrostep ? "MULTISTEP PUNCH" : "PUNCH");

            steps.Add(new SimulationStep(
                StepNumber: currentStep++,
                Delta: currentMaterialDelta,
                StepDisplacement: stepDisplacement,
                FrontPosition: group.First().AbsoluteFrontX,
                PunchesMask: combinedMask,
                IsCutActive: isCutActive,
                CutTargetX: optimalKnifeX,
                Info: info
            ));
        }

        return steps;
    }

    /// <summary>
    /// [7] KINEMATYKA MASZYNY (WYZNACZANIE DELTY): Równanie transformacji osi.
    /// 
    /// Matematyka:
    /// Układ maszyny zakłada punkt 0.0 na IDEALNYM ŚRODKU (indeks 15.5).
    /// Oś współrzędnych stempla względem zera: X_masz_stempla = (Indeks - 15.5) * Skok_Maszyny
    /// Współrzędna absolutna otworu w materiale: X_abs_otworu = X_front + X_otworu_w_detalu.
    /// Aby stempel trafił w otwór, czoło materiału (Δ_maszyny) musi zostać wciągnięte na pozycję:
    /// Δ_maszyny = X_abs_otworu + X_masz_stempla
    /// </summary>
    private double CalculateMachineDelta(double absoluteDetailFrontX, double holeXOnDetail, int punchIndex)
    {
        double punchAxisPosition = (punchIndex - _machine.MachineCenterIndex) * _machine.Pitch;
        double absoluteHolePosition = absoluteDetailFrontX + holeXOnDetail;
        return absoluteHolePosition + punchAxisPosition;
    }

    private record PunchPass(List<int> ActivePunchIndices, double ReferenceHoleX, int ReferencePunchIndex, bool IsMicrostep);

    /// <summary>
    /// [8] ALGORYTM PRZEPLOTU (INTERLEAVING): Mapowanie fizycznych otworów na uderzenia matrycy.
    /// Posiada Dynamiczny Ogranicznik Kinematyczny zapobiegający ruchom w tył prasy.
    /// 
    /// Matematyka Ogranicznika:
    /// Jeśli proporcja narzędzi wynosi 'ratio', to minimalny stempel dla kolejnego przeplotu (p),
    /// gwarantujący dodatni posuw maszyny, określa wzór: minRequired = Ceil(HighestPunch(p-1) - ratio).
    /// </summary>
    private List<PunchPass> CalculatePassesForChunk(List<double> chunkHoles, double holePitch, int minPunch, int maxPunch)
    {
        var passes = new List<PunchPass>();
        double ratio = holePitch / _machine.Pitch;
        int availablePunches = maxPunch - minPunch + 1;
        double subsetCenterIndex = minPunch + (availablePunches - 1) / 2.0;

        if (FindRationalRatio(ratio, out int A, out int B))
        {
            if (A == 0) A = 1;
            var subHolesList = new List<List<double>>();
            var idealHighest = new int[B];

            // A: Generowanie idealnych przeplotów (każdy uderza możliwie najbliżej środka strefy)
            for (int p = 0; p < B; p++)
            {
                var subHoles = new List<double>();
                for (int i = p; i < chunkHoles.Count; i += B) subHoles.Add(chunkHoles[i]);
                subHolesList.Add(subHoles);

                if (subHoles.Count > 0)
                {
                    int span = (subHoles.Count - 1) * A;
                    // Centrowanie wokół lokalnego środka podzespołu (np. między indeksem 16 a 31 = 23.5)
                    idealHighest[p] = (int)Math.Max(minPunch, Math.Round(subsetCenterIndex + (span / 2.0), MidpointRounding.AwayFromZero));
                    if (idealHighest[p] > maxPunch) idealHighest[p] = maxPunch;
                }
            }

            var actualHighest = new int[B];
            if (subHolesList[0].Count > 0)
            {
                // Pierwszy przeplot uderza bezkarnie w cel (Ideal)
                actualHighest[0] = idealHighest[0];
                passes.Add(CreatePass(subHolesList[0], A, actualHighest[0]));

                // Kolejne przeploty muszą negocjować z Fizyką (Zakaz Cofania)
                for (int p = 1; p < B; p++)
                {
                    if (subHolesList[p].Count > 0)
                    {
                        int minRequired = (int)Math.Ceiling(actualHighest[p - 1] - ratio);
                        actualHighest[p] = Math.Max(idealHighest[p], minRequired); // Negocjacja: co większe (ideał vs twarda fizyka)
                        if (actualHighest[p] > maxPunch) actualHighest[p] = maxPunch;
                        passes.Add(CreatePass(subHolesList[p], A, actualHighest[p]));
                    }
                }
            }
        }
        else
        {
            // Tryb awaryjny mikrokroku (Fallback): Niezidentyfikowany skok. 
            // Maszyna bije jednym centralnym stęplem (krok 1 narzędzia) i przesuwa materiał powoli.
            int centerPunch = (int)Math.Floor(subsetCenterIndex);
            for (int i = 0; i < chunkHoles.Count; i++)
            {
                passes.Add(new PunchPass(new List<int> { centerPunch }, chunkHoles[i], centerPunch, true));
            }
        }
        return passes;
    }

    /// <summary>
    /// Metoda pomocnicza tworząca gotowy zestaw indeksów bitowych dla danego kroku przeplotu.
    /// </summary>
    private PunchPass CreatePass(List<double> holes, int punchStride, int highestPunch)
    {
        var indices = new List<int>();
        for (int i = 0; i < holes.Count; i++)
        {
            indices.Add(highestPunch - i * punchStride); // Indeksy spadają: Prawa strona -> Lewa strona
        }
        return new PunchPass(indices, holes[0], highestPunch, false);
    }

    /// <summary>
    /// PREKALKULATOR GILOTYNY (PREDICTIVE SHEAR): Symuluje ruch maszyny i wylicza dokładny X dla nożyc.
    /// 
    /// Matematyka:
    /// Gilotyna nie może zatrzymywać materiału (strata czasu). Musi zgrywać się z otworowaniem.
    /// Cel Noża (CutTarget) to środek rzazu na granicy detali: X_rzazu = L_detalu + W_noża / 2.
    /// Poszukujemy takiego podziału 'Chunks', by przy M-tej iteracji detalu:
    /// X_noża = Δ_maszyny_w_momencie_bicia_stempla(M) - X_rzazu ∈ [ShearMin, ShearMax].
    /// </summary>
    public double CalculateOptimalKnifePosition(DetailConfig detail, out int optimalChunks)
    {
        List<double> holePositions = CalculateHolePositions(detail);

        int minPunch = _machine.EnableSerration ? _machine.SerrationMaxPunches : 0;
        int maxPunch = _machine.MaxPunches - 1;
        int availablePunches = maxPunch - minPunch + 1;

        int effectiveMaxPunches = GetEffectiveMaxPunches(detail.HolePitch, availablePunches);

        int minChunks = (int)Math.Ceiling((double)holePositions.Count / effectiveMaxPunches);
        if (minChunks <= 0) minChunks = 1;

        int maxPossibleChunks = Math.Max(1, holePositions.Count);
        double detailTotalLength = detail.Length + _machine.BladeWidth;
        double kerfOffset = detail.Length + (_machine.BladeWidth / 2.0);

        for (int testChunks = minChunks; testChunks <= maxPossibleChunks; testChunks++)
        {
            List<int> chunkSizes = CalculateBalancedChunks(holePositions.Count, effectiveMaxPunches, testChunks);

            var allPasses = new List<PunchPass>();
            int holeIndex = 0;
            foreach (int chunkSize in chunkSizes)
            {
                var chunkHoles = holePositions.GetRange(holeIndex, chunkSize);
                holeIndex += chunkSize;
                allPasses.AddRange(CalculatePassesForChunk(chunkHoles, detail.HolePitch, minPunch, maxPunch));
            }

            List<double> validPassDeltas = new List<double>();
            foreach (var pass in allPasses)
            {
                if (!pass.IsMicrostep)
                {
                    validPassDeltas.Add(CalculateMachineDelta(0, pass.ReferenceHoleX, pass.ReferencePunchIndex));
                }
            }

            if (validPassDeltas.Count == 0 && allPasses.Count > 0)
            {
                validPassDeltas.Add(CalculateMachineDelta(0, allPasses.Last().ReferenceHoleX, allPasses.Last().ReferencePunchIndex));
            }

            // Rozciągamy badanie posuwu dla M symulowanych detali w przód.
            for (int M = 0; M < 20; M++)
            {
                foreach (double passDelta in validPassDeltas)
                {
                    double currentMachineDelta = passDelta + (M * detailTotalLength);
                    double knifeX = currentMachineDelta - kerfOffset;

                    if (knifeX >= _machine.ShearMin && knifeX <= _machine.ShearMax)
                    {
                        optimalChunks = testChunks;
                        return knifeX;
                    }
                }
            }
        }

        // Zabezpieczenie awaryjne dla niewymiarowych/ekstremalnych płaskowników
        optimalChunks = minChunks;
        return Math.Max(_machine.ShearMin, Math.Min(2000.0, _machine.ShearMax));
    }

    /// <summary>
    /// Oblicza faktyczną wydajność strefy biorąc pod uwagę odciążenie wynikające z przeplotów.
    /// Wydajność = (Max_dostępnych_stempli / Zębatka_A) * Przeploty_B
    /// </summary>
    private int GetEffectiveMaxPunches(double holePitch, int availablePunches)
    {
        double ratio = holePitch / _machine.Pitch;
        if (FindRationalRatio(ratio, out int A, out int B))
        {
            if (A == 0) A = 1;
            int maxIntervals = (availablePunches - 1) / A;
            int punchesPerHit = maxIntervals + 1;

            return punchesPerHit * B;
        }
        return availablePunches;
    }

    /// <summary>
    /// ANALIZATOR PRZEKŁADNI (FRACTION DETECTOR): Poszukuje ułamkowego wzorca między skokiem otworów a stempli.
    /// 
    /// Matematyka:
    /// Szuka ułamka A/B ≈ ratio, gdzie B ∈ [1, 20]. Zapewnia to identyfikację skoków takich jak:
    /// 11.1 / 33.3 = 1/3 (A=1, B=3 -> maszyna wykona to w 3 fazach używając co 1 stempla)
    /// 44.4 / 33.3 = 4/3 (A=4, B=3 -> maszyna wykona to w 3 fazach używając co 4 stempla)
    /// </summary>
    private bool FindRationalRatio(double ratio, out int A, out int B, double tolerance = 0.01)
    {
        for (int b = 1; b <= 20; b++)
        {
            double aDouble = b * ratio;
            int a = (int)Math.Round(aDouble);

            if (Math.Abs(aDouble - a) <= tolerance)
            {
                A = a;
                B = b;
                return true;
            }
        }
        A = 1; B = 1;
        return false;
    }

    /// <summary>
    /// [11] GENERATOR SERATACJI (PHASE SHIFTER): Oblicza półokrągłe nacięcia na krawędzi detalu.
    /// 
    /// Matematyka:
    /// Seratacja stosuje sztuczne przesunięcie fazowe w przestrzeni detalu: Offset = 11.1 / 2 = 5.55 mm.
    /// Marginesy są rygorystycznie limitowane i ucinane w dół (Floor), aby zapobiec uderzeniom o skraj krawędzi blachy.
    /// Ilość bezpiecznych nacięć na marginesie = Floor(Rzeczywisty_Margines / Stały_Skok_Seratacji).
    /// </summary>
    public List<double> CalculateSerrationPositions(DetailConfig detail, List<double> standardPositions)
    {
        var serrations = new HashSet<double>();
        if (standardPositions.Count == 0) return new List<double>();

        double anchorFirst = standardPositions.First();
        double anchorLast = standardPositions.Last();
        double radius = _machine.SerrationWidth / 2.0;

        // 1. Ośrodek Seratacji (użycie konfiguracji z pliku)
        double pos = anchorFirst + _machine.SerrationOffset;
        while (pos < anchorLast)
        {
            serrations.Add(pos);
            pos += _machine.SerrationPitch;
        }

        // =================================================================
        // NOWY ALGORYTM: Dynamiczne wyliczanie ilości na podstawie Typu P / T
        // =================================================================
        double actualMarginRight = anchorFirst; //
        double actualMarginLeft = detail.Length - anchorLast; //

        int rightSerrationsCount = 0;
        int leftSerrationsCount = 0;

        if (detail.Type == CuttingType.P)
        {
            // Reguła dla typu P (Próg bazowy 10, krok 11.1)
            rightSerrationsCount = actualMarginRight < 10.0 ? 0 : (int)Math.Floor((actualMarginRight - 10.0) / _machine.SerrationPitch) + 1;
            leftSerrationsCount = actualMarginLeft < 10.0 ? 0 : (int)Math.Floor((actualMarginLeft - 10.0) / _machine.SerrationPitch) + 1;
        }
        else // CuttingType.T
        {
            // Reguła dla typu T (Próg bazowy 20.5, krok 11.1)
            rightSerrationsCount = actualMarginRight < 20.5 ? 0 : (int)Math.Floor((actualMarginRight - 20.5) / _machine.SerrationPitch) + 1;
            leftSerrationsCount = actualMarginLeft < 20.5 ? 0 : (int)Math.Floor((actualMarginLeft - 20.5) / _machine.SerrationPitch) + 1;
        }


        // 2. Seratacje na prawym marginesie
        //int rightSerrationsCount = (int)Math.Floor(anchorFirst / _machine.SerrationPitch);
        pos = anchorFirst - _machine.SerrationOffset;

        for (int i = 0; i < rightSerrationsCount; i++)
        {
            if (pos >= radius)
            {
                serrations.Add(pos);
            }
            pos -= _machine.SerrationPitch;
        }

        // 3. Seratacje na lewym marginesie
        //double actualMarginLeft = detail.Length - anchorLast;
        //int leftSerrationsCount = (int)Math.Floor(actualMarginLeft / _machine.SerrationPitch);
        pos = anchorLast + _machine.SerrationOffset;

        for (int i = 0; i < leftSerrationsCount; i++)
        {
            if (pos <= detail.Length - radius)
            {
                serrations.Add(pos);
            }
            pos += _machine.SerrationPitch;
        }

        var list = serrations.ToList();
        list.Sort();
        return list;
    }
}