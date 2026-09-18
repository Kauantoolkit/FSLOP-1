using System.Globalization;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fslop.SpikeB.EditorTools
{
    /// <summary>
    /// Constroi o player do soak.
    ///
    /// Por que um PLAYER e nao mais uma sonda de edit mode: tudo que esta task mediu ate
    /// aqui saiu de Physics.Simulate em edit mode, que e deterministico e barato — mas nao
    /// tem loop de quadro, e portanto nao tem fps. O briefing pede "60fps estaveis no
    /// host", e isso so existe num processo que desenha.
    ///
    /// Por que COM graficos: um player -nographics nao renderiza, entao "quadros no ultimo
    /// segundo" viraria taxa de loop. Seria um numero grande, bonito e sobre outra coisa.
    ///
    /// Rodar: Unity.exe -projectPath ... -batchmode -quit -nographics
    ///        -executeMethod Fslop.SpikeB.EditorTools.SpikePlayerBuilder.Build
    /// </summary>
    public static class SpikePlayerBuilder
    {
        public const string SaidaDir = "Build/SpikeB";
        public const string SaidaExe = SaidaDir + "/FslopSpikeB.exe";

        public const string SaidaDirDev = "Build/SpikeB-dev";
        public const string SaidaExeDev = SaidaDirDev + "/FslopSpikeB.exe";

        [MenuItem("FSLOP/Construir player do soak")]
        public static void Build()
        {
            Construir(desenvolvimento: false);
        }

        /// <summary>
        /// Build de DESENVOLVIMENTO, em diretorio proprio. Existe por um motivo unico e
        /// medido: o simulador de latencia do FishNet so e consultado atras de
        /// `#if DEVELOPMENT`, que o TransportManager define como
        /// `UNITY_EDITOR || DEVELOPMENT_BUILD` (TransportManager.cs:1-3). Sem esta versao,
        /// os 150 ms e 3% que o briefing manda simular sao aceitos e ignorados.
        ///
        /// DIRETORIO SEPARADO de proposito. Se as duas sobrescrevessem o mesmo .exe, a
        /// corrida seguinte usaria a versao errada sem nada acusar — e as duas NAO sao
        /// intercambiaveis: development build nao tem stripping e carrega hooks de profiler,
        /// entao o fps dela nao vale como fps do produto. Ver docs/99 item 20.
        /// </summary>
        [MenuItem("FSLOP/Construir player do soak (development)")]
        public static void BuildDevelopment()
        {
            Construir(desenvolvimento: true);
        }

        static void Construir(bool desenvolvimento)
        {
            var opcoes = new BuildPlayerOptions
            {
                scenes = new[] { SpikeSceneBuilder.ScenePath },
                locationPathName = desenvolvimento ? SaidaExeDev : SaidaExe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = desenvolvimento ? BuildOptions.Development : BuildOptions.None,
            };

            Log("flavor=" + (desenvolvimento ? "development" : "release"));

            var relatorio = BuildPipeline.BuildPlayer(opcoes);
            var resumo = relatorio.summary;

            Log(string.Format(CultureInfo.InvariantCulture,
                "resultado={0} tamanho_bytes={1} erros={2} avisos={3} duracao_s={4:F1} saida={5}",
                resumo.result, resumo.totalSize, resumo.totalErrors, resumo.totalWarnings,
                resumo.totalTime.TotalSeconds, resumo.outputPath));

            if (resumo.result != BuildResult.Succeeded)
            {
                // Sem isto o batchmode sai 0 com build quebrada, e a corrida seguinte roda
                // um .exe velho sem ninguem perceber.
                Debug.LogError("[PLAYER] build NAO teve sucesso: " + resumo.result);
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        static void Log(string mensagem)
        {
            Debug.Log("[PLAYER] " + mensagem);
        }
    }
}
