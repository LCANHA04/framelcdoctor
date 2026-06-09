namespace FrameLCDoctor;

/// <summary>Concrete, actionable in-game settings to change for more fps, by bottleneck.</summary>
public static class SettingsAdvisor
{
    public static (string title, string[] items) Advise(string bottleneck, bool fixedTimestep = false)
    {
        // A fixed-timestep engine (e.g. NieR) speeds up if uncapped, so the 'cap' advice
        // must warn against removing it instead of recommending it.
        if (bottleneck == "cap" && fixedTimestep)
            return ("Este juego tiene paso fijo (su motor ata la velocidad a los fps):",
                new[]
                {
                    "NO saques el cap: arriba de su fps base el juego se ACELERA",
                    "Quedate en su fps de diseno (normalmente 60)",
                    "Si querer mas fps, necesita un mod que arregle el timestep",
                });
        return AdviseInner(bottleneck);
    }

    private static (string title, string[] items) AdviseInner(string bottleneck) => bottleneck switch
    {
        "cpu-single" or "cpu-multi" =>
            ("Estas frenado por el procesador. Baja lo que genera trabajo de CPU (la resolucion no ayuda, la GPU te sobra):",
            new[]
            {
                "Distancia de dibujado / view distance",
                "Densidad de objetos, follaje y particulas",
                "Sombras dinamicas: distancia y cantidad",
                "Densidad de NPCs / multitudes",
                "Fisica, ragdolls, destruccion",
                "Cerra apps de fondo (navegador, overlays)",
                "Si el juego lo permite, activa DX12 / modo multinucleo",
            }),

        "gpu" =>
            ("Estas frenado por la placa de video. Baja lo que carga la GPU:",
            new[]
            {
                "Resolucion o escala de renderizado",
                "Activa upscaling (DLSS / FSR / XeSS) si esta disponible",
                "Anti-aliasing (el MSAA es lo mas caro)",
                "Resolucion de sombras y reflejos",
                "Post-procesado, oclusion ambiental, volumetricos",
                "Calidad de texturas (si te falta VRAM)",
            }),

        "cap" =>
            ("Tu fps esta topeado, no es el hardware:",
            new[]
            {
                "Saca el limite de fps (en el juego o en el driver)",
                "Desactiva el vsync si no lo necesitas",
                "Ojo: motores de paso fijo se aceleran arriba de su fps base",
            }),

        "balanced" =>
            ("GPU y CPU bien aprovechadas: queda poco margen.",
            new[]
            {
                "Ganancias chicas: baja 1 o 2 settings pesados",
                "Proba upscaling para aliviar la GPU",
            }),

        _ => ("Conecta un juego para ver que conviene cambiar.", Array.Empty<string>()),
    };
}
