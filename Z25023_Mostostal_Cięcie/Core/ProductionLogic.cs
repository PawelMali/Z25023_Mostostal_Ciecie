using System;
using System.Collections.Generic;
using System.Text;

namespace Z25023_Mostostal_Cięcie.Core;

public class ProductionLogic
{
    private readonly MachineConfig _machine;

    // Fizyczne parametry maszyny zdefiniowane jako stałe

    public ProductionLogic(MachineConfig machineConfig)
    {
        _machine = machineConfig;
    }

    /// <summary>
    /// Oblicza absolutną pozycję czoła detalu względem punktu X=0 (czoło D1).
    /// Uwzględnia szerokość noża (odrzut materiału) po każdym cięciu.
    /// </summary>
    /// <param name="detailIndex">Indeks detalu (0 dla D1, 1 dla D2, itd.)</param>
    public double GetAbsoluteFrontPosition(int detailIndex, DetailConfig detail)
    {
        // Dla D1 (index 0) pozycja to 0.
        // Dla D2 (index 1) pozycja to Długość detalu + Szerokość noża.
        return detailIndex * (detail.Length + _machine.BladeWidth);
    }


    /// <summary>
    /// Weryfikuje warunek cięcia (Eager Cut) dla bieżącej absolutnej pozycji płaskownika w maszynie.
    /// Zwraca true, jeśli cięcie może być wykonane w locie (SYNC).
    /// </summary>
    /// <param name="currentMaterialPosition">Aktualne przesunięcie materiału w maszynie (Delta sumaryczna)</param>
    /// <param name="cutTargetPosition">Absolutna pozycja na płaskowniku, w której ma nastąpić cięcie (np. koniec detalu D1)</param>
    /// <param name="requiresStopStep">Flaga wyjściowa (out) informująca, czy przekroczono ShearMax i wymagany jest krok STOP</param>
    public bool CheckShearCondition(double currentMaterialDelta, double cutTargetPosition, out bool requiresStopStep)
    {
        requiresStopStep = false;

        // Gdzie w maszynie fizycznie znajduje się linia odcięcia płaskownika
        double cutLineMachineX = currentMaterialDelta - cutTargetPosition;

        // Sprawdzamy, czy linia cięcia wjechała w strefę nożyc (np. 1850.7)
        if (cutLineMachineX >= _machine.ShearMin && cutLineMachineX <= _machine.ShearMax)
        {
            return true;
        }

        // Jeśli materiał wyjechał za daleko (linia cięcia minęła max zasięg)
        if (cutLineMachineX > _machine.ShearMax)
        {
            requiresStopStep = true;
        }

        return false;
    }

    /// <summary>
    /// Konwertuje listę indeksów stempli (0-31) na maskę bitową zgodną z Siemens DWORD.
    /// </summary>
    /// <param name="activePunches">Kolekcja indeksów używanych stempli (np. [14, 15, 16, 17])</param>
    /// <returns>32-bitowa wartość uint reprezentująca maskę narzędzi</returns>
    public uint GeneratePunchMask(IEnumerable<int> activePunches)
    {
        uint mask = 0;
        foreach (var punchIndex in activePunches)
        {
            if (punchIndex >= 0 && punchIndex < _machine.MaxPunches)
            {
                // Ustawienie bitu na 1 na odpowiedniej pozycji
                mask |= (1u << punchIndex);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(punchIndex), $"Indeks stempla {punchIndex} poza zakresem maszyny!");
            }
        }
        return mask;
    }

    /// <summary>
    /// Dzieli zrównoważenie zadania. Pozwala na wymuszenie większej liczby paczek (forcedChunks),
    /// co jest kluczowe, gdy standardowy podział nie pozwala na trafienie w strefę cięcia.
    /// </summary>
    public List<int> CalculateBalancedChunks(int totalHoles, int maxActivePunches, int forcedChunks = 0)
    {
        if (totalHoles <= 0) return new List<int>();

        int numberOfChunks = forcedChunks > 0 ? forcedChunks : (int)Math.Ceiling((double)totalHoles / maxActivePunches);
        if (numberOfChunks <= 0) numberOfChunks = 1;

        // Zabezpieczenie przed podziałem na więcej paczek niż jest otworów
        if (numberOfChunks > totalHoles) numberOfChunks = totalHoles;

        int baseSize = totalHoles / numberOfChunks;
        int remainder = totalHoles % numberOfChunks;

        var chunks = new List<int>(numberOfChunks);
        for (int i = 0; i < numberOfChunks; i++)
        {
            chunks.Add(baseSize + (i < remainder ? 1 : 0));
        }

        return chunks;
    }



    /// <summary>
    /// Generuje listę współrzędnych (X) ŚRODKÓW otworów dla pojedynczego detalu względem jego CZOŁA (0.0).
    /// Uwzględnia grubość narzędzia (PunchWidth), aby zachować zadeklarowane marginesy fizyczne do krawędzi wycięcia.
    /// </summary>
    public List<double> CalculateHolePositions(DetailConfig detail)
    {
        var positions = new List<double>();

        // Odległość od środka stempla do jego krawędzi
        double punchRadius = _machine.PunchWidth / 2.0;

        // Bezpieczna baza - odległość od krawędzi płaskownika do ŚRODKA pierwszego narzędzia
        double centerMarginRight = detail.MarginRight + punchRadius;
        double centerMarginLeft = detail.MarginLeft + punchRadius;

        // Dostępna przestrzeń na umieszczenie środków kolejnych otworów
        double availableLength = detail.Length - centerMarginRight - centerMarginLeft;

        if (availableLength < 0) return positions; // Narzędzie nie zmieści się z zachowaniem marginesów
        if (detail.HolePitch <= 0) throw new ArgumentException("Skok otworów musi być większy od zera.");

        // Liczba pełnych skoków
        int numberOfIntervals = (int)Math.Floor(availableLength / detail.HolePitch);
        int numberOfHoles = numberOfIntervals + 1;

        // BAZOWANIE NA PRAWYM MARGINESIE:
        for (int i = 0; i < numberOfHoles; i++)
        {
            // Pozycja to środek narzędzia
            positions.Add(centerMarginRight + (i * detail.HolePitch));
        }

        return positions;
    }


    /// <summary>
    /// Główna metoda generująca kroki dla danego detalu.
    /// </summary>
    /// <param name="detail">Parametry detalu wejściowego</param>
    /// <param name="absoluteDetailFrontX">Absolutna pozycja czoła tego detalu w maszynie</param>
    /// <param name="startStepNumber">Numer początkowy kroku dla zachowania ciągłości w tabeli</param>
    public List<SimulationStep> GenerateStepsForDetail(DetailConfig detail, double absoluteDetailFrontX, int startStepNumber, ref double lastAbsoluteDelta, double optimalKnifeX, int forcedChunks)
    {
        var steps = new List<SimulationStep>();
        int currentStep = startStepNumber;

        List<double> holePositions = CalculateHolePositions(detail);

        int effectiveMaxPunches = GetEffectiveMaxPunches(detail.HolePitch);
        List<int> chunkSizes = CalculateBalancedChunks(holePositions.Count, effectiveMaxPunches, forcedChunks);

        var allPasses = new List<PunchPass>();
        int holeIndex = 0;
        foreach (int chunkSize in chunkSizes)
        {
            var chunkHoles = holePositions.GetRange(holeIndex, chunkSize);
            holeIndex += chunkSize;

            // Usunięto chunkHoles.Reverse()
            allPasses.AddRange(CalculatePassesForChunk(chunkHoles, detail.HolePitch));
        }

        double detailTotalLength = detail.Length + _machine.BladeWidth;

        for (int i = 0; i < allPasses.Count; i++)
        {
            var pass = allPasses[i];
            uint mask = GeneratePunchMask(pass.ActivePunchIndices);
            double currentMaterialDelta = CalculateMachineDelta(absoluteDetailFrontX, pass.ReferenceHoleX, pass.ReferencePunchIndex);

            double stepDisplacement = currentMaterialDelta - lastAbsoluteDelta;
            if (currentStep == 1) stepDisplacement = 0.0;
            lastAbsoluteDelta = currentMaterialDelta;

            bool isCutActive = false;
            if (!pass.IsMicrostep)
            {
                double absoluteMaterialUnderKnife = currentMaterialDelta - optimalKnifeX;
                double shifted = absoluteMaterialUnderKnife + (_machine.BladeWidth / 2.0);
                double multiple = Math.Round(shifted / detailTotalLength);
                double nearestKerfShifted = multiple * detailTotalLength;

                if (Math.Abs(shifted - nearestKerfShifted) < 0.1)
                {
                    isCutActive = true;
                }
            }

            string info = isCutActive ? "PUNCH & SYNC CUT" : (pass.IsMicrostep ? "MULTISTEP PUNCH" : "PUNCH");

            steps.Add(new SimulationStep(
                StepNumber: currentStep++,
                Delta: currentMaterialDelta,
                StepDisplacement: stepDisplacement,
                FrontPosition: absoluteDetailFrontX,
                PunchesMask: mask,
                IsCutActive: isCutActive,
                CutTargetX: optimalKnifeX,
                Info: info
            ));
        }

        return steps;
    }

    /// <summary>
    /// Wylicza fizyczny przesuw materiału maszyny (Delta).
    /// Punkt 0 osi X znajduje się teraz idealnie na ŚRODKU prasy (MachineCenterIndex).
    /// </summary>
    private double CalculateMachineDelta(double absoluteDetailFrontX, double holeXOnDetail, int punchIndex)
    {
        // Stemple po lewej od środka mają X ujemne, po prawej X dodatnie.
        double punchAxisPosition = (punchIndex - _machine.MachineCenterIndex) * _machine.Pitch;

        double absoluteHolePosition = absoluteDetailFrontX + holeXOnDetail;

        // Pozycja czoła materiału w maszynie (Delta) tak, aby otwór zrównał się ze stemplem
        return absoluteHolePosition + punchAxisPosition;
    }

    // Deklaracja pomocniczej struktury dla przejść (mikrokroków)
    private record PunchPass(List<int> ActivePunchIndices, double ReferenceHoleX, int ReferencePunchIndex, bool IsMicrostep);


    /// <summary>
    /// Grupuje otwory w paczce na konkretne uderzenia (Passes).
    /// Używa Dynamicznego Ogranicznika Kinematycznego (Forward-Only), 
    /// maksymalizując centrowanie narzędzi bez łamania fizyki prasy.
    /// </summary>
    private List<PunchPass> CalculatePassesForChunk(List<double> chunkHoles, double holePitch)
    {
        var passes = new List<PunchPass>();
        double ratio = holePitch / _machine.Pitch;

        if (FindRationalRatio(ratio, out int A, out int B))
        {
            if (A == 0) A = 1;

            var subHolesList = new List<List<double>>();
            var idealHighest = new int[B];

            // 1. Zbieramy przeploty i obliczamy IDEALNY środek dla każdego z nich niezależnie
            for (int p = 0; p < B; p++)
            {
                var subHoles = new List<double>();
                for (int i = p; i < chunkHoles.Count; i += B) subHoles.Add(chunkHoles[i]);
                subHolesList.Add(subHoles);

                if (subHoles.Count > 0)
                {
                    int span = (subHoles.Count - 1) * A;
                    idealHighest[p] = (int)Math.Max(0, Math.Round(_machine.MachineCenterIndex + (span / 2.0), MidpointRounding.AwayFromZero));
                    if (idealHighest[p] >= _machine.MaxPunches) idealHighest[p] = _machine.MaxPunches - 1;
                }
            }

            var actualHighest = new int[B];
            if (subHolesList[0].Count > 0)
            {
                // Pierwszy przeplot zawsze może uderzyć idealnie w środek
                actualHighest[0] = idealHighest[0];
                passes.Add(CreatePass(subHolesList[0], A, actualHighest[0]));

                // 2. KINEMATYCZNY BEZPIECZNIK dla kolejnych przeplotów
                for (int p = 1; p < B; p++)
                {
                    if (subHolesList[p].Count > 0)
                    {
                        // Minimalny wymóg stempla gwarantujący, że Delta (przesuw) będzie dodatnia
                        int minRequired = (int)Math.Ceiling(actualHighest[p - 1] - ratio);

                        // Wybieramy stempel najbliżej idealnego środka, omijając zakaz cofania
                        actualHighest[p] = Math.Max(idealHighest[p], minRequired);
                        if (actualHighest[p] >= _machine.MaxPunches) actualHighest[p] = _machine.MaxPunches - 1;

                        passes.Add(CreatePass(subHolesList[p], A, actualHighest[p]));
                    }
                }
            }
        }
        else
        {
            // Mikrokrok awaryjny dla całkowicie nieregularnych podziałek
            int centerPunch = (int)Math.Floor(_machine.MachineCenterIndex);
            for (int i = 0; i < chunkHoles.Count; i++)
            {
                passes.Add(new PunchPass(new List<int> { centerPunch }, chunkHoles[i], centerPunch, true));
            }
        }

        // Ostateczne sortowanie gwarantujące ruch tylko do przodu
        passes.Sort((p1, p2) =>
        {
            double delta1 = CalculateMachineDelta(0, p1.ReferenceHoleX, p1.ReferencePunchIndex);
            double delta2 = CalculateMachineDelta(0, p2.ReferenceHoleX, p2.ReferencePunchIndex);
            return delta1.CompareTo(delta2);
        });

        return passes;
    }


    /// <summary>
    /// Tworzy mapowanie narzędzi dla danego przeplotu na podstawie wyznaczonego górnego stempla.
    /// </summary>
    private PunchPass CreatePass(List<double> holes, int punchStride, int highestPunch)
    {
        var indices = new List<int>();
        for (int i = 0; i < holes.Count; i++)
        {
            indices.Add(highestPunch - i * punchStride);
        }
        return new PunchPass(indices, holes[0], highestPunch, false);
    }

    /// <summary>
    /// Wylicza idealną pozycję noża. Jeśli standardowy podział uderzeń nie zgra rzazu ze strefą nożyc,
    /// algorytm zwiększa liczbę podziałów (chunks), aż fizyka maszyny "wepchnie" cięcie w strefę.
    /// </summary>
    public double CalculateOptimalKnifePosition(DetailConfig detail, out int optimalChunks)
    {
        List<double> holePositions = CalculateHolePositions(detail);

        int effectiveMaxPunches = GetEffectiveMaxPunches(detail.HolePitch);
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

                // KRYTYCZNA ZMIANA: Usunięto chunkHoles.Reverse(). 
                // Przekazujemy otwory naturalnie od czoła do tyłu.
                allPasses.AddRange(CalculatePassesForChunk(chunkHoles, detail.HolePitch));
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

        optimalChunks = minChunks;
        return Math.Max(_machine.ShearMin, Math.Min(2000.0, _machine.ShearMax));
    }


    /// <summary>
    /// Wylicza rzeczywistą maksymalną ilość otworów w jednym rzucie.
    /// Uwzględnia przeploty (Interleaving) dla dowolnej podziałki proporcjonalnej.
    /// </summary>
    private int GetEffectiveMaxPunches(double holePitch)
    {
        double ratio = holePitch / _machine.Pitch;

        if (FindRationalRatio(ratio, out int A, out int B))
        {
            if (A == 0) A = 1; // Zabezpieczenie fizyki

            // Ile stempli fizycznie mieści się na szynie, gdy używamy co A-tego stempla?
            int punchesPerHit = Math.Max(1, _machine.MaxPunches / A);

            // Mnożymy to przez ilość przeplotów (B)
            return punchesPerHit * B;
        }

        return _machine.MaxPunches;
    }



    /// <summary>
    /// Szuka uniwersalnej proporcji (przekładni) między skokiem otworu a skokiem maszyny.
    /// Zwraca A (krok stempli) oraz B (ilość przeplotów/cykli).
    /// </summary>
    private bool FindRationalRatio(double ratio, out int A, out int B, double tolerance = 0.01)
    {
        // B to nasz mianownik (ilość przeplotów blachy). Szukamy optymalnego do max 20 uderzeń.
        for (int b = 1; b <= 20; b++)
        {
            double aDouble = b * ratio;
            int a = (int)Math.Round(aDouble);

            // Jeśli wynik jest bliski liczbie całkowitej, znaleźliśmy idealne zębatki!
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
}


