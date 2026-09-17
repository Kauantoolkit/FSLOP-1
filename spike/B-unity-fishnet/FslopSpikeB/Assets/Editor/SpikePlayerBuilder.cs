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

        [MenuItem("FSLOP/Construir player do soak")]
        public static void Build()
        {
            var opcoes = new BuildPlayerOptions
            {
                scenes = new[] { SpikeSceneBuilder.ScenePath },
                locationPathName = SaidaExe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

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
