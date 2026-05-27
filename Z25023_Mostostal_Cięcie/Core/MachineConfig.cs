using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Text.Json;

namespace Z25023_Mostostal_Cięcie.Core;

/// <summary>
/// Reprezentuje stałe i konfigurowalne parametry fizyczne prasy.
/// </summary>
public record MachineConfig
{
    public double ShearMin { get; init; } = 1906; // Zakres minimalnego położenia noża cięcia, który prasa jest w stanie obsłużyć
    public double ShearMax { get; init; } = 2790; // Zakres maksymalnego położenia noża cięcia, który prasa jest w stanie obsłużyć
    public double BladeWidth { get; init; } = 6.0;   // Grubość noża (rzaz)
    public double Pitch { get; init; } = 33.3;       // Skok między stemplami standardowymi
    public double MachineCenterIndex { get; init; } = 15.5; // Indeks centralny maszyny
    public int MaxPunches { get; init; } = 32;       // Maksymalna liczba stempli, które prasa może obsłużyć (32 dla 2 rzędów po 16)
    public double PunchWidth { get; init; } = 1.5;   // Szerokość stempla standardowego

    // PARAMETRY SERATACJI :
    public bool EnableSerration { get; init; } = false;
    public double SerrationWidth { get; init; } = 7.0; // wielkośc oczka seratacji głownie do wizualizacji
    public double SerrationPitch { get; init; } = 11.1;     
    public int SerrationMaxPunches { get; init; } = 16;     // Liczba narzędzi w strefie seratacji

    // Właściwość obliczana automatycznie - brak konieczności konfiguracji przez operatora
    public double SerrationOffset => SerrationPitch / 2.0;


    //Czas trwania jednego pełnego cyklu prasy (w sekundach)
    public double CycleTimeSeconds { get; init; } = 2.01;

    /// <summary>
    /// Pobiera konfigurację z pliku machine_config.json. 
    /// W przypadku braku pliku tworzy domyślny szablon struktury.
    /// </summary>
    public static MachineConfig Load()
    {
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "machine_config.json");
        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<MachineConfig>(json);
                if (config != null) return config;
            }

            // Samoleczenie systemu (Best Practice): generujemy czysty szablon konfiguracji dla technologa
            var defaultConfig = new MachineConfig();
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(filePath, JsonSerializer.Serialize(defaultConfig, options));
            return defaultConfig;
        }
        catch
        {
            // Fallback: w przypadku uszkodzenia pliku tekstowego przez operatora, system wstanie na bezpiecznych domyślnych
            return new MachineConfig();
        }
    }
}
