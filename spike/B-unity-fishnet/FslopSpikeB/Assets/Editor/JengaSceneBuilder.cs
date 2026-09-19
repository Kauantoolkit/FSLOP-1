using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Fslop.SpikeB.Jogo;

namespace Fslop.SpikeB.EditorTools
{
    /// <summary>
    /// Monta a cena do BRINQUEDO — nao do spike.
    ///
    /// Por que existe: em 19/09 o usuario pediu "alguma simulacao de jogo com o harness para
    /// ver se fica divertido". O briefing proibe adicionar mecanica de jogo ao spike, e o
    /// pedido veio de quem escreveu o briefing. A saida e separar: o spike continua com as
    /// cenas dele, intactas e com os numeros valendo, e isto vive ao lado sem tocar nelas.
    ///
    /// NENHUM arquivo do spike referencia nada daqui. A checagem e simples: se apagar
    /// Assets/Scripts/Jogo/ e este arquivo, o spike compila e roda igual.
    ///
    /// O QUE ESTA CENA NAO TEM, de proposito: rede. E fisica local, para a pergunta ser "isso
    /// tem graca?" e nao "isso tem graca apesar do drift?". Rede entra depois de a resposta
    /// da primeira ser sim.
    ///
    /// Rodar: Unity.exe -projectPath ... -batchmode -quit -nographics
    ///        -executeMethod Fslop.SpikeB.EditorTools.JengaSceneBuilder.Build
    /// </summary>
    public static class JengaSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Jenga.unity";

        const float GroundSide = 40f;
        const float GroundThickness = 1f;

        /// <summary>Quantas zonas, e portanto quantos jogadores o cenario espera.</summary>
        const int Zonas = 4;

        /// <summary>Raio de cada zona e do circulo em que elas ficam.</summary>
        const float RaioDaZona = 3.5f;
        const float RaioDoCirculo = 11f;

        /// <summary>
        /// Caixas soltas no meio, que e de onde todo mundo tira. Menos que as 150 do teste
        /// minimo de proposito: o ponto aqui e carregar e empilhar, e 150 caixas no chao
        /// viram um tapete em que ninguem anda.
        /// </summary>
        const int CaixasNoMonte = 60;

        /// <summary>
        /// Raio do monte central. Ver a conta de ocupacao em CriarMonteDeCaixas: com 60
        /// caixas de 1 u^2, abaixo de ~6 u elas nascem sobrepostas.
        /// </summary>
        const float RaioDoMonte = 7f;

        [MenuItem("FSLOP/Gerar cena do Jenga (brinquedo)")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CriarChao();
            CriarSol();
            CriarCamera();
            CriarMonteDeCaixas();
            CriarZonas();
            CriarJogador();

            var relator = new GameObject("JengaReport");
            relator.AddComponent<JengaReport>();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new System.Exception("[JENGA] SaveScene falhou para " + ScenePath);
            }

            AssetDatabase.SaveAssets();
            Log("cena escrita em " + ScenePath + " (raizes=" + scene.rootCount + ")");
        }

        static void CriarChao()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ground";
            go.transform.localScale = new Vector3(GroundSide, GroundThickness, GroundSide);
            go.transform.position = new Vector3(0f, -GroundThickness * 0.5f, 0f);
        }

        static void CriarSol()
        {
            var go = new GameObject("Sun");
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            go.AddComponent<Light>().type = LightType.Directional;
        }

        static void CriarCamera()
        {
            var go = new GameObject("MainCamera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 7f, -11f);
            go.AddComponent<Camera>();
            go.AddComponent<FollowCamera>();
        }

        /// <summary>
        /// Monte central, espalhado num disco. Posicoes deterministicas: duas sessoes
        /// comecam iguais, o que e o minimo para duas pessoas compararem o que sentiram.
        /// </summary>
        static void CriarMonteDeCaixas()
        {
            var raiz = new GameObject("MonteDeCaixas");

            for (int i = 0; i < CaixasNoMonte; i++)
            {
                // Espiral de Fermat: distribui num disco sem aglomerar no centro, e e uma
                // formula, nao um sorteio — entao nao muda entre sessoes.
                //
                // O RAIO E CONTA, nao gosto: area = pi*R^2, e cada caixa ocupa 1 u^2. Com
                // R = 4.5 (a primeira versao) dava 63.6 u^2 para 60 caixas — 94% de
                // ocupacao, ou seja, elas NASCERIAM interpenetradas e o PhysX as arremessaria
                // no primeiro passo. Com R = 7 a area vai a 154 u^2 e o espacamento medio a
                // sqrt(154/60) = 1.6 u, folgado para caixas de 1 u.
                float t = i / (float)CaixasNoMonte;
                float raio = RaioDoMonte * Mathf.Sqrt(t);
                float ang = i * 2.39996323f;

                var caixa = GameObject.CreatePrimitive(PrimitiveType.Cube);
                caixa.name = string.Format(CultureInfo.InvariantCulture, "Caixa_{0:D2}", i);
                caixa.transform.SetParent(raiz.transform, false);
                caixa.transform.position = new Vector3(
                    Mathf.Cos(ang) * raio, 0.5f + (i % 3) * 0.02f, Mathf.Sin(ang) * raio);

                var corpo = caixa.AddComponent<Rigidbody>();
                corpo.mass = 10f;
            }
        }

        static void CriarZonas()
        {
            for (int i = 0; i < Zonas; i++)
            {
                float ang = i * Mathf.PI * 2f / Zonas;
                var go = new GameObject("Zona_" + (i + 1));
                go.transform.position = new Vector3(
                    Mathf.Cos(ang) * RaioDoCirculo, 0f, Mathf.Sin(ang) * RaioDoCirculo);

                go.AddComponent<StackZone>().Configure("jogador_" + (i + 1), RaioDaZona);

                // Marca visivel, achatada e SEM colisor: e para a pessoa ver onde e a area
                // dela, nao para empurrar caixa.
                var marca = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marca.name = "Marca";
                marca.transform.SetParent(go.transform, false);
                marca.transform.localScale = new Vector3(RaioDaZona * 2f, 0.02f, RaioDaZona * 2f);
                Object.DestroyImmediate(marca.GetComponent<Collider>());
            }
        }

        static void CriarJogador()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Player";
            go.transform.position = new Vector3(0f, 3f, -7f);

            // Material SEM ATRITO, o mesmo do spike. Nao e detalhe: o CapsuleMotor controla
            // a velocidade horizontal por inteiro, e o atrito do PhysX vira um SEGUNDO
            // controlador no mesmo eixo. Medido em decisions/08: tira mu*g*dt =
            // 0.6*9.81*0.02 = 0.1177 u/s da velocidade comandada a cada passo.
            //
            // A primeira versao desta cena esqueceu isto, e andar teria parecido arrastado
            // — por um motivo ja medido e ja resolvido do outro lado do projeto. As CAIXAS
            // continuam com atrito normal, que e o que permite empilhar.
            go.GetComponent<CapsuleCollider>().sharedMaterial =
                SpikeSceneBuilder.CreatePlayerMaterial();

            var corpo = go.AddComponent<Rigidbody>();
            corpo.mass = 70f;
            corpo.interpolation = RigidbodyInterpolation.Interpolate;
            corpo.freezeRotation = true;

            go.AddComponent<CapsuleMotor>();
            go.AddComponent<CapsuleController>();
            go.AddComponent<Grabber>();
            go.AddComponent<GrabberInput>();
            go.AddComponent<SeguirJogador>();
        }

        public const string SaidaExe = "Build/Jenga/Jenga.exe";

        /// <summary>
        /// Player do brinquedo, em diretorio proprio.
        ///
        /// Nao toca em EditorBuildSettings de proposito: quem mexe naquilo e o gerador do
        /// spike, e as duas coisas nao podem disputar a mesma configuracao global. Aqui a
        /// cena vai direto nas opcoes do build.
        /// </summary>
        [MenuItem("FSLOP/Construir player do Jenga (brinquedo)")]
        public static void BuildPlayer()
        {
            Build();

            var opcoes = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = SaidaExe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            var resumo = BuildPipeline.BuildPlayer(opcoes).summary;

            Log(string.Format(CultureInfo.InvariantCulture,
                "player: resultado={0} erros={1} avisos={2} saida={3}",
                resumo.result, resumo.totalErrors, resumo.totalWarnings, resumo.outputPath));

            if (resumo.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.LogError("[JENGA] build NAO teve sucesso: " + resumo.result);
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        static void Log(string m)
        {
            Debug.Log("[JENGA] " + m);
        }
    }
}
