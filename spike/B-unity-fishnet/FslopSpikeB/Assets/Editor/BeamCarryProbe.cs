using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fslop.SpikeB.EditorTools
{
    /// <summary>
    /// Mede o agarre da viga de 6 m com 1, 2, 3 e 4 maos, e o efeito do dial de massScale.
    ///
    /// Isto e a metade LOCAL da change 08: fisica de uma instancia so, sem rede e sem
    /// latencia. E pre-requisito, nao resposta — a pergunta do briefing ("a viga carregada
    /// nao teleporta sob 150 ms de RTT") so pode ser respondida com dois relogios, e isso e
    /// a metade seguinte. Se a viga ja balanca sozinha aqui, nao adianta testar em rede.
    ///
    /// carry_jump_u e o campo do docs/00-contrato-de-medicao.md: maior salto de posicao da
    /// viga entre dois quadros CONSECUTIVOS. O limiar de reprovacao la e 0.5 u.
    ///
    /// Rodar: Unity.exe -projectPath ... -batchmode -quit -nographics
    ///        -executeMethod Fslop.SpikeB.EditorTools.BeamCarryProbe.Run
    /// </summary>
    public static class BeamCarryProbe
    {
        const int SettleSteps = 120;      // 2.4 s para maos e viga assentarem antes do agarre
        const int GrabSteps = 100;        // 2 s parado, so a mola levantando a viga
        const int CarrySteps = 150;       // 3 s andando com ela
        const float CarryJumpLimitU = 0.5f;

        static float dt;
        static string failure;

        public static void Run()
        {
            failure = null;

            var modoAnterior = Physics.simulationMode;
            Log("simulationMode ao entrar=" + modoAnterior);

            try
            {
                Execute();
            }
            finally
            {
                Physics.simulationMode = modoAnterior;
                Log("simulationMode ao sair=" + Physics.simulationMode);
            }

            if (failure != null)
            {
                Debug.LogError("[VIGA] FAIL " + failure);
                EditorApplication.Exit(1);
                return;
            }

            Log("PASS");
            EditorApplication.Exit(0);
        }

        static void Execute()
        {
            var scene = EditorSceneManager.OpenScene(SpikeSceneBuilder.ScenePath, OpenSceneMode.Single);
            dt = Time.fixedDeltaTime;

            var beamGo = Find(scene, SpikeSceneBuilder.BeamName);
            var playerGo = Find(scene, SpikeSceneBuilder.PlayerName);
            if (failure != null)
            {
                return;
            }

            var beam = beamGo.GetComponent<CarryBeam>();
            if (beam == null)
            {
                Fail("o objeto " + SpikeSceneBuilder.BeamName + " nao tem CarryBeam");
                return;
            }

            Log(F("viga comprimento={0:F2} u massa={1:F1} kg mola={2:F0} damper={3:F0} dt={4:F4}",
                CarryBeam.LengthU, beam.MassKg, beam.Spring, beam.Damper, dt));

            Physics.simulationMode = SimulationMode.Script;

            // 1) Quantas maos levantam a viga, e ela treme?
            for (int maos = 1; maos <= 4; maos++)
            {
                if (!Carry(beamGo, beam, playerGo, maos, 1f, 1f, "maos", false))
                {
                    return;
                }
            }

            // 2) O dial de decisions/09: quanto a viga puxa o portador de volta.
            //    O nome massScale escala a massa INVERSA, entao a direcao do efeito e medida
            //    aqui em vez de afirmada. 4 maos, que e o caso do briefing.
            foreach (float escala in new[] { 0.05f, 1f, 20f })
            {
                if (!Carry(beamGo, beam, playerGo, 4, escala, 1f, "dial", false))
                {
                    return;
                }
            }

            // 3) Agarre ASSIMETRICO, com as maos juntas em UMA ponta. Sem esta corrida o
            //    campo inclinacao_graus e zero por construcao: as corridas 1 e 2 distribuem
            //    as maos simetricamente em torno do centro, e viga simetrica nao tomba.
            //    Publicar aquele zero como "a viga nao inclina" seria mentira — quem carrega
            //    uma viga de 6 m em jogo pega onde der.
            for (int maos = 1; maos <= 2; maos++)
            {
                if (!Carry(beamGo, beam, playerGo, maos, 1f, 1f, "ponta", true))
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Uma corrida: cria as maos, deixa assentar, agarra, espera a mola levantar, anda,
        /// mede. Devolve false se algo reprovou.
        /// </summary>
        static bool Carry(GameObject beamGo, CarryBeam beam, GameObject playerGo,
            int maos, float holderMassScale, float beamMassScale, string rotulo, bool naPonta)
        {
            // Estado inicial identico em toda corrida: sem isso nao da para comparar as
            // linhas da tabela entre si.
            beam.ReleaseAll();
            beamGo.transform.position = new Vector3(-10f, 0.25f, 0f);
            beamGo.transform.rotation = Quaternion.identity;
            beam.Body.linearVelocity = Vector3.zero;
            beam.Body.angularVelocity = Vector3.zero;
            beam.Configure(beam.MassKg, beam.Spring, beam.Damper, holderMassScale, beamMassScale);

            var portadores = CriarPortadores(playerGo, beamGo, maos, naPonta);

            for (int i = 0; i < SettleSteps; i++)
            {
                PassoDosPortadores(portadores, default);
                Physics.Simulate(dt);
            }

            for (int i = 0; i < portadores.Count; i++)
            {
                Vector3 mao = portadores[i].transform.TransformPoint(new Vector3(0f, 0.5f, 0.5f));
                beam.Grab(portadores[i].GetComponent<Rigidbody>(), mao);
            }

            float alturaAntes = beamGo.transform.position.y;

            var medida = Simular(beamGo, portadores, GrabSteps, default);
            float alturaLevantada = beamGo.transform.position.y;

            // Linha de base do atraso: a folga com o portador PARADO, depois de a mola
            // assentar. Tudo o que a marcha fizer se mede como desvio disto.
            float folgaParado = medida.FolgaFim;

            Vector3 vigaInicio = beamGo.transform.position;
            Vector3 maoInicio = portadores[0].transform.position;

            var andando = Simular(beamGo, portadores, CarrySteps, new MoveIntent { Move = new Vector2(0f, 1f) });

            // Inclinacao do eixo longo em relacao ao horizonte. Via asin do componente y do
            // eixo normalizado, e nao por Vector3.Angle contra a projecao: com a viga na
            // vertical a projecao e o vetor zero, e Angle devolveria 0 — que se leria como
            // "nao inclinou" justamente no caso pior.
            float inclinacao = Mathf.Asin(Mathf.Clamp01(Mathf.Abs(beamGo.transform.right.normalized.y)))
                               * Mathf.Rad2Deg;

            // Deslocamentos ficam no log como CONTROLE, nao como atraso: se as duas
            // distancias batem, as velocidades batem, e entao todo o atraso esta na folga
            // — que e onde ele vive.
            float deslocViga = Horizontal(beamGo.transform.position - vigaInicio);
            float deslocMao = Horizontal(portadores[0].transform.position - maoInicio);

            // O atraso da viga em relacao a mao e a evidencia central de decisions/09: se a
            // viga ja chega atrasada em jogo local, o atraso da rede se esconde dentro dele.
            // O atraso e o quanto a folga se AFASTA do valor parado durante a marcha, e o
            // pior caso e o que importa. Em segundos: essa distancia dividida por 4 u/s.
            float atrasoU = Mathf.Max(Mathf.Abs(andando.FolgaMin - folgaParado),
                                      Mathf.Abs(andando.FolgaMax - folgaParado));
            float atrasoMs = atrasoU / 4f * 1000f;

            // Quanto da folga sobrou no FIM da marcha: se voltou ao valor parado, o atraso e
            // so transitorio (arranque); se ficou deslocada, ha atraso em regime.
            float atrasoRegimeU = Mathf.Abs(andando.FolgaFim - folgaParado);

            float jumpMax = Mathf.Max(medida.JumpMax, andando.JumpMax);
            bool estourou = float.IsNaN(beamGo.transform.position.y)
                            || Mathf.Abs(beamGo.transform.position.y) > 100f;

            Log(F("VIGA[{0}] maos={1} massScale_mao={2:F2} altura_pos_agarre={3:F4} " +
                  "subiu_u={4:F4} inclinacao_graus={5:F2} carry_jump_max_u={6:F4} " +
                  "carry_jump_p99_u={7:F4} desloc_viga_u={8:F4} desloc_mao_u={9:F4} " +
                  "folga_parado_u={10:F4} atraso_pico_u={11:F4} atraso_pico_ms={12:F1} " +
                  "atraso_regime_u={13:F4} estourou={14}",
                rotulo, maos, holderMassScale, alturaLevantada, alturaLevantada - alturaAntes,
                inclinacao, jumpMax, Mathf.Max(medida.JumpP99, andando.JumpP99),
                deslocViga, deslocMao, folgaParado, atrasoU, atrasoMs, atrasoRegimeU,
                estourou ? 1 : 0));

            beam.ReleaseAll();
            DestruirPortadores(portadores);

            if (estourou)
            {
                Fail(F("a viga estourou com {0} maos (massScale {1:F2})", maos, holderMassScale));
                return false;
            }

            if (jumpMax > CarryJumpLimitU)
            {
                Fail(F("carry_jump_u={0:F4} passou do limiar {1:F2} do contrato, com {2} maos " +
                       "e SEM rede nenhuma", jumpMax, CarryJumpLimitU, maos));
                return false;
            }

            return true;
        }

        struct Medida
        {
            public float JumpMax;
            public float JumpP99;

            /// <summary>
            /// Folga no eixo da marcha (+z) entre a viga e a mao do portador 0: positivo =
            /// viga a frente da mao. E ESTA a grandeza do atraso. A tentativa anterior,
            /// subtrair deslocamentos percorridos, media diferenca de VELOCIDADE — e em
            /// regime as duas velocidades sao iguais por construcao, porque o CapsuleMotor
            /// reescreve a velocidade do portador todo passo. Atraso e deslocamento
            /// constante, nao distancia perdida.
            /// </summary>
            public float FolgaMin;
            public float FolgaMax;
            public float FolgaFim;
        }

        static Medida Simular(GameObject beamGo, List<GameObject> portadores, int passos, MoveIntent intent)
        {
            var saltos = new List<float>(passos);
            Vector3 anterior = beamGo.transform.position;

            float folgaMin = float.PositiveInfinity;
            float folgaMax = float.NegativeInfinity;
            float folgaFim = 0f;

            for (int i = 0; i < passos; i++)
            {
                PassoDosPortadores(portadores, intent);
                Physics.Simulate(dt);

                Vector3 agora = beamGo.transform.position;
                saltos.Add(Vector3.Distance(agora, anterior));
                anterior = agora;

                folgaFim = Folga(beamGo, portadores);
                folgaMin = Mathf.Min(folgaMin, folgaFim);
                folgaMax = Mathf.Max(folgaMax, folgaFim);
            }

            saltos.Sort();

            return new Medida
            {
                JumpMax = saltos.Count > 0 ? saltos[saltos.Count - 1] : 0f,
                JumpP99 = saltos.Count > 0
                    ? saltos[Mathf.Clamp(Mathf.CeilToInt(0.99f * saltos.Count) - 1, 0, saltos.Count - 1)]
                    : 0f,
                FolgaMin = float.IsInfinity(folgaMin) ? 0f : folgaMin,
                FolgaMax = float.IsInfinity(folgaMax) ? 0f : folgaMax,
                FolgaFim = folgaFim,
            };
        }

        /// <summary>
        /// Distancia no eixo da marcha entre a viga e o ponto da mao do portador 0 — o mesmo
        /// ponto que o SpringJoint usa como ancora, pedido ao CarryBeam em vez de remontado
        /// aqui.
        /// </summary>
        static float Folga(GameObject beamGo, List<GameObject> portadores)
        {
            if (portadores.Count == 0)
            {
                return 0f;
            }

            var beam = beamGo.GetComponent<CarryBeam>();
            Vector3 mao = portadores[0].transform.TransformPoint(beam.HandAnchor);

            return beamGo.transform.position.z - mao.z;
        }

        static void PassoDosPortadores(List<GameObject> portadores, MoveIntent intent)
        {
            foreach (var portador in portadores)
            {
                var motor = portador.GetComponent<CapsuleMotor>();
                if (motor != null)
                {
                    motor.Step(intent, dt);
                }
            }
        }

        /// <summary>
        /// Os portadores sao COPIAS do Player da cena, nao capsulas montadas aqui. Assim
        /// eles herdam massa, material sem atrito e motor exatamente iguais aos do jogador
        /// medido nas changes 03 e 04 — se eu remontasse a capsula na sonda, estaria medindo
        /// outro personagem.
        /// </summary>
        static List<GameObject> CriarPortadores(GameObject playerGo, GameObject beamGo,
            int quantos, bool naPonta)
        {
            var lista = new List<GameObject>(quantos);
            float meia = CarryBeam.LengthU * 0.5f - 0.5f;

            for (int i = 0; i < quantos; i++)
            {
                // Simetrico: maos espalhadas de -meia a +meia. Na ponta: encostadas umas nas
                // outras a partir de -meia, espacadas por 1 u (o diametro da capsula), que e
                // o mais junto que dois jogadores conseguem ficar.
                float x = naPonta
                    ? -meia + i * 1f
                    : Mathf.Lerp(-meia, meia, quantos <= 1 ? 0.5f : i / (float)(quantos - 1));

                var copia = Object.Instantiate(playerGo);
                copia.name = "Holder_" + i;

                // Encostados na viga pelo lado -z, na altura de repouso ja medida (y=1.0).
                copia.transform.position = beamGo.transform.position + new Vector3(x, 0.75f, -0.9f);
                copia.transform.rotation = Quaternion.identity;

                var body = copia.GetComponent<Rigidbody>();
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;

                lista.Add(copia);
            }

            return lista;
        }

        static void DestruirPortadores(List<GameObject> portadores)
        {
            foreach (var portador in portadores)
            {
                Object.DestroyImmediate(portador);
            }

            portadores.Clear();
        }

        static GameObject Find(Scene scene, string nome)
        {
            foreach (var raiz in scene.GetRootGameObjects())
            {
                if (raiz.name == nome)
                {
                    return raiz;
                }
            }

            Fail("objeto ausente na cena: " + nome);
            return null;
        }

        /// <summary>
        /// Comprimento de um deslocamento no PLANO, ignorando y. O atraso que interessa e
        /// o horizontal: a viga tambem sobe quando a mola a levanta, e somar essa subida
        /// ao deslocamento de marcha inflaria o numero.
        /// </summary>
        static float Horizontal(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }

        static string F(string formato, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, formato, args);
        }

        static void Log(string mensagem)
        {
            Debug.Log("[VIGA] " + mensagem);
        }

        static void Fail(string motivo)
        {
            if (failure == null)
            {
                failure = motivo;
            }
        }
    }
}
