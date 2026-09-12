using Xunit;

// F8-01: TODAS las pruebas de este proyecto invocan "dotnet new install"/"dotnet new <shortName>" contra
// el mismo caché de plantillas del usuario (~/.templateengine) -- correrlas en paralelo (comportamiento
// por defecto de xunit: una colección implícita por clase, colecciones distintas en paralelo entre sí)
// puede deadlockear ese caché compartido entre procesos "dotnet" concurrentes (comprobado empíricamente
// al agregar AppTemplateVerificationTests: el CPU de los procesos "dotnet test"/"vstest.console" caía a
// ~0 indefinidamente, nunca progresaba). Deshabilita la paralelización a nivel de assembly -- estas
// pruebas son lentas mas sí confiables, y esto no afecta a NINGÚN otro proyecto de test del repositorio.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
