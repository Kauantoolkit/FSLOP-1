using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fslop.SpikeB.EditorTools
{
    /// <summary>
    /// Mede o que o CLIENTE VE da viga carregada, para as quatro abordagens de decisions/09.
    ///
    /// ISTO NAO E UMA REDE. E o modelo determinístico de decisions/10: o host roda de
    /// verdade (Physics.Simulate, as mesmas molas da changes/07), a trajetoria da viga e
    /// gravada passo a passo, e sobre a trajetoria gravada aplica-se o que a rede faz com
    /// ela — amostragem, atraso, perda e interpolacao. Todo numero daqui sai rotulado
    /// [MODELO]. Nao prova transporte, nao mede jitter, nao mede banda.
    ///
    /// A diferenca para a BeamCarryProbe importa: la o carry_jump_u e medido entre passos
    /// de FISICA; aqui e medido entre QUADROS RENDERIZADOS do cliente, que e o que o
    /// jogador enxerga. O limiar de 0.5 u do docs/00 e sobre o que se ve.
    ///
    /// Rodar: Unity.exe -projectPath ... -batchmode -quit -nographics
    ///        -executeMethod Fslop.SpikeB.EditorTools.NetViewProbe.Run
    /// </summary>
    public static class NetViewProbe
    {
        const int SettleSteps = 120;   // 2.4 s assentando
        const int GrabSteps = 100;     // 2 s parado, mola levantando
        const int LegSteps = 75;       // 1.5 s por perna do percurso
        const int Maos = 4;            // o caso do briefing

        // Perfil de rede de decisions/01. RTT FIXO — rede real varia, e variacao de atraso
        // e o que mais fabrica salto: este numero e otimista por construcao.
        const float RttMs = 150f;
        const float Perda = 0.03f;
        const float SnapshotHz = 20f;
        const float RenderHz = 60f;

        /// <summary>
        /// Buffer de interpolacao: o cliente desenha atrasado de propósito, para ter sempre
        /// dois snapshots em volta do instante desenhado e poder interpolar em vez de
        /// extrapolar. Um intervalo de snapshot e o minimo que cumpre isso. E ESCOLHA, nao
        /// medicao — e e a escolha que mais mexe no resultado, por isso esta varrida abaixo.
        /// </summary>
        const float BufferSnapshots = 1f;

        /// <summary>Semente fixa: a perda de pacote tem que ser reproduzivel entre corridas.</summary>
        const int Semente = 12345;

        const float CarryJumpLimitU = 0.5f;

        static float dt;
        static string failure;

        /// <summary>Quadros descartados por a tubulacao de snapshots ainda estar enchendo.</summary>
        static int aquecimento;

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
                Debug.LogError("[REDE] FAIL " + failure);
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

            int passosRtt = Mathf.RoundToInt(RttMs / 1000f / dt);
            int passosMeioRtt = Mathf.RoundToInt(RttMs / 2000f / dt);

            Log(F("[MODELO] perfil rtt_ms={0:F0} perda={1:F2} snapshot_hz={2:F0} " +
                  "render_hz={3:F0} buffer_snapshots={4:F1} semente={5} dt={6:F4} " +
                  "passos_rtt={7} passos_meio_rtt={8}",
                RttMs, Perda, SnapshotHz, RenderHz, BufferSnapshots, Semente, dt,
                passosRtt, passosMeioRtt));

            Physics.simulationMode = SimulationMode.Script;

            var trilha = GravarTrilha(beamGo, beam, playerGo, passosMeioRtt);
            if (failure != null)
            {
                return;
            }

            Log(F("[MODELO] trilha gravada: {0} passos de fisica = {1:F2} s de percurso, " +
                  "com inversao de sentido no passo {2}",
                trilha.Viga.Count, trilha.Viga.Count * dt, LegSteps));

            // Piso: o que o cliente veria com rede PERFEITA (zero atraso, zero perda,
            // snapshot a cada passo). Tudo acima disto e custo de rede, nao de fisica.
            var piso = ReamostrarDireto(trilha.Viga);
            Publicar("0-sem-rede", piso, 0f,
                "piso teorico: rede perfeita, so reamostragem para 60 Hz");

            // Abordagem 4, a escolhida: cliente prediz so o proprio personagem e recebe a
            // viga replicada. A viga na tela e a trilha do host, atrasada e interpolada.
            var a4 = Renderizar(trilha.Viga, RttMs / 2f, Perda, SnapshotHz, BufferSnapshots);
            Publicar("4-mola", a4, 0f,
                "cliente sem joint; viga interpolada do host; personagem predito");

            // Abordagem 1: o personagem tambem passa a ser desenhado pelo host durante o
            // agarre. A viga fica IGUAL a da 4 — o que muda e o teclado, que vira o RTT.
            var a1 = Renderizar(trilha.Viga, RttMs / 2f, Perda, SnapshotHz, BufferSnapshots);
            Publicar("1-suspende-predicao", a1, RttMs,
                "viga identica a da 4; o que reprova e input_ms, por aritmetica");

            // Abordagem 3: a viga vira filha da mao. O cliente a desenha a partir do
            // PROPRIO personagem predito, entao nao ha atraso nem perda nenhuma sobre ela.
            var a3 = ReamostrarDireto(trilha.Mao);
            Publicar("3-cinematico", a3, 0f,
                "viga presa ao personagem predito; sem rede no meio; NAO suporta 4 donos");

            // Abordagem 2: o cliente prediz a viga junto. O salto que ele leva nao vem do
            // atraso, vem da RECONCILIACAO — a diferenca entre o que ele previu e o que o
            // host diz que aconteceu.
            PublicarPrevisao("2-prediz-par", trilha, passosMeioRtt);

            // Varredura da abordagem escolhida. O perfil de decisions/01 (150 ms / 3%) passa,
            // mas passar num ponto nao diz quanta folga existe — e o modelo e otimista
            // (RTT fixo, sem jitter). Isto mede ONDE ela quebra, que e o que serve para
            // decidir se ela aguenta rede real.
            foreach (float rtt in new[] { 50f, 100f, 150f, 250f, 400f })
            {
                var v = Renderizar(trilha.Viga, rtt / 2f, Perda, SnapshotHz, BufferSnapshots);
                Publicar(F("4-varre-rtt-{0:F0}", rtt), v, 0f, F("rtt={0:F0} perda=0.03", rtt));
            }

            foreach (float perda in new[] { 0f, 0.03f, 0.10f, 0.25f })
            {
                var v = Renderizar(trilha.Viga, RttMs / 2f, perda, SnapshotHz, BufferSnapshots);
                Publicar(F("4-varre-perda-{0:F2}", perda), v, 0f, F("rtt=150 perda={0:F2}", perda));
            }

            foreach (float hz in new[] { 10f, 20f, 30f, 60f })
            {
                var v = Renderizar(trilha.Viga, RttMs / 2f, Perda, hz, BufferSnapshots);
                Publicar(F("4-varre-hz-{0:F0}", hz), v, 0f, F("snapshot_hz={0:F0} rtt=150", hz));
            }

            foreach (float buf in new[] { 0f, 1f, 2f, 3f })
            {
                var v = Renderizar(trilha.Viga, RttMs / 2f, Perda, SnapshotHz, buf);
                Publicar(F("4-varre-buffer-{0:F0}", buf), v, 0f,
                    F("buffer={0:F0} snapshots = {1:F0} ms a mais de atraso na tela",
                        buf, buf * 1000f / SnapshotHz));
            }
        }

        // ------------------------------------------------------------------ host

        class Trilha
        {
            public readonly List<Vector3> Viga = new List<Vector3>();
            public readonly List<Vector3> Mao = new List<Vector3>();

            /// <summary>
            /// Divergencia da abordagem 2: em cada ponto de checagem, o quanto a viga
            /// prevista pelo cliente errou contra a verdade do host meio-RTT depois.
            /// </summary>
            public readonly List<float> Divergencias = new List<float>();

            /// <summary>Controle: a mesma previsao com input de todos conhecido. Deve dar ~0.</summary>
            public readonly List<float> DivergenciasOraculo = new List<float>();
        }

        /// <summary>
        /// Roda o host de verdade e grava. O percurso tem INVERSAO DE SENTIDO no meio de
        /// proposito: changes/07 mostrou que o atraso da viga e transitorio — ele existe no
        /// arranque e some em regime —, entao medir so linha reta mede o caso facil.
        /// </summary>
        static Trilha GravarTrilha(GameObject beamGo, CarryBeam beam, GameObject playerGo,
            int passosMeioRtt)
        {
            var trilha = new Trilha();
            previsoes.Clear();

            beam.ReleaseAll();
            beamGo.transform.position = new Vector3(-10f, 0.25f, 0f);
            beamGo.transform.rotation = Quaternion.identity;
            beam.Body.linearVelocity = Vector3.zero;
            beam.Body.angularVelocity = Vector3.zero;
            beam.Configure(beam.MassKg, beam.Spring, beam.Damper, 1f, 1f);

            var portadores = CriarPortadores(playerGo, beamGo, Maos);

            for (int i = 0; i < SettleSteps; i++)
            {
                Passo(portadores, default);
                Physics.Simulate(dt);
            }

            for (int i = 0; i < portadores.Count; i++)
            {
                Vector3 mao = portadores[i].transform.TransformPoint(beam.HandAnchor);
                beam.Grab(portadores[i].GetComponent<Rigidbody>(), mao);
            }

            for (int i = 0; i < GrabSteps; i++)
            {
                Passo(portadores, default);
                Physics.Simulate(dt);
            }

            var corpos = Corpos(beamGo, portadores);
            int total = LegSteps * 2;

            for (int i = 0; i < total; i++)
            {
                // Ponto de checagem da abordagem 2, ANTES de avancar: o cliente tem o estado
                // confirmado deste instante e precisa desenhar meio-RTT a frente.
                if (i + passosMeioRtt < total)
                {
                    var salvo = Salvar(corpos);
                    Vector3 previsto = Prever(beamGo, portadores, passosMeioRtt, i, false);
                    Restaurar(corpos, salvo);

                    // CONTROLE: a mesma previsao com o cliente sabendo o input de todos. O
                    // resultado tem que ser ~0. Se nao for, o que o numero de cima mede e o
                    // erro do meu Salvar/Restaurar — que nao repoe o estado interno do
                    // solver — e nao o erro de previsao da abordagem 2.
                    Vector3 oraculo = Prever(beamGo, portadores, passosMeioRtt, i, true);
                    Restaurar(corpos, salvo);

                    // Indice com -1 porque trilha.Viga[k] guarda a posicao DEPOIS do passo k:
                    // prever N passos a partir do estado anterior ao passo i cai em
                    // trilha.Viga[i + N - 1]. Sem o -1 a divergencia media um passo inteiro
                    // de deslocamento da viga, e nao o erro da previsao.
                    previsoes.Add(new Previsao
                    {
                        Passo = i + passosMeioRtt - 1,
                        Posicao = previsto,
                        Oraculo = oraculo,
                    });
                }

                PassoRoteiro(portadores, i);
                Physics.Simulate(dt);

                trilha.Viga.Add(beamGo.transform.position);
                trilha.Mao.Add(portadores[0].transform.TransformPoint(beam.HandAnchor));
            }

            trilha.Divergencias.Clear();
            trilha.DivergenciasOraculo.Clear();

            foreach (var p in previsoes)
            {
                if (p.Passo >= 0 && p.Passo < trilha.Viga.Count)
                {
                    trilha.Divergencias.Add(Vector3.Distance(p.Posicao, trilha.Viga[p.Passo]));
                    trilha.DivergenciasOraculo.Add(Vector3.Distance(p.Oraculo, trilha.Viga[p.Passo]));
                }
            }

            beam.ReleaseAll();
            Destruir(portadores);

            return trilha;
        }

        struct Previsao
        {
            public int Passo;
            public Vector3 Posicao;
            public Vector3 Oraculo;
        }

        static readonly List<Previsao> previsoes = new List<Previsao>();

        /// <summary>
        /// O palpite do cliente: ele conhece o proprio input, e para os OUTROS tres so tem o
        /// ultimo input confirmado. Aqui isso e modelado mantendo o intent do instante da
        /// checagem para todos menos o portador 0 — que e exatamente o que faz a previsao
        /// errar na inversao de sentido, quando os outros viram e o cliente ainda nao sabe.
        /// </summary>
        static Vector3 Prever(GameObject beamGo, List<GameObject> portadores,
            int passos, int passoAtual, bool oraculo)
        {
            for (int k = 0; k < passos; k++)
            {
                int passoReal = passoAtual + k;

                for (int p = 0; p < portadores.Count; p++)
                {
                    var motor = portadores[p].GetComponent<CapsuleMotor>();
                    if (motor == null)
                    {
                        continue;
                    }

                    // O cliente conhece o proprio input em tempo real. Dos outros tres, so
                    // tem o ULTIMO confirmado — congelado no instante da checagem. O oraculo
                    // e o controle: todos com o input verdadeiro.
                    bool conheceDeVerdade = p == 0 || oraculo;
                    motor.Step(IntentDe(p, conheceDeVerdade ? passoReal : passoAtual), dt);
                }

                Physics.Simulate(dt);
            }

            return beamGo.transform.position;
        }

        // ------------------------------------------------ estado (salvar / restaurar)

        struct EstadoCorpo
        {
            public Vector3 Pos;
            public Quaternion Rot;
            public Vector3 Vel;
            public Vector3 AngVel;
        }

        static List<Rigidbody> Corpos(GameObject beamGo, List<GameObject> portadores)
        {
            var lista = new List<Rigidbody> { beamGo.GetComponent<Rigidbody>() };

            foreach (var p in portadores)
            {
                lista.Add(p.GetComponent<Rigidbody>());
            }

            return lista;
        }

        static EstadoCorpo[] Salvar(List<Rigidbody> corpos)
        {
            var estado = new EstadoCorpo[corpos.Count];

            for (int i = 0; i < corpos.Count; i++)
            {
                estado[i] = new EstadoCorpo
                {
                    Pos = corpos[i].position,
                    Rot = corpos[i].rotation,
                    Vel = corpos[i].linearVelocity,
                    AngVel = corpos[i].angularVelocity,
                };
            }

            return estado;
        }

        /// <summary>
        /// Restaura pose e velocidade. NAO restaura o estado interno do solver do PhysX —
        /// caches de contato, warm start. Por isso o galho de previsao e curto (meio RTT) e
        /// a divergencia daqui e um piso, nao um teto.
        /// </summary>
        static void Restaurar(List<Rigidbody> corpos, EstadoCorpo[] estado)
        {
            for (int i = 0; i < corpos.Count; i++)
            {
                corpos[i].position = estado[i].Pos;
                corpos[i].rotation = estado[i].Rot;
                corpos[i].linearVelocity = estado[i].Vel;
                corpos[i].angularVelocity = estado[i].AngVel;
            }
        }

        // ------------------------------------------------------------------ cliente

        /// <summary>
        /// Aplica sobre a trilha gravada o que a rede faz com ela e devolve o que o cliente
        /// desenha, quadro a quadro: amostra a snapshotHz, atrasa atrasoMs, perde pacote com
        /// probabilidade perda, e interpola entre os dois snapshots que sobraram em volta do
        /// instante desenhado. Quando nao sobra snapshot a frente, SEGURA o ultimo — que e o
        /// que produz o congelamento seguido de salto, o efeito que se quer flagrar.
        /// </summary>
        /// <summary>
        /// Reamostra a trajetoria de fisica (50 Hz) na taxa de quadros (60 Hz), interpolando
        /// entre passos. Sem rede no meio: nem snapshot, nem atraso, nem perda.
        ///
        /// Existe separada de propósito. Passar o caso "sem rede" pelo modelo de rede com os
        /// parametros zerados NAO da o mesmo resultado: sem buffer o cliente nao tem passo
        /// futuro para interpolar e segura o ultimo, o que fabricava 31 quadros congelados
        /// num cenario que por definicao nao tem perda nenhuma.
        /// </summary>
        static List<Vector3> ReamostrarDireto(List<Vector3> trilha)
        {
            var quadros = new List<Vector3>();
            aquecimento = 0;
            float duracao = trilha.Count * dt;

            for (float t = 0f; t <= duracao; t += 1f / RenderHz)
            {
                float passo = t / dt;
                int a = Mathf.Clamp(Mathf.FloorToInt(passo), 0, trilha.Count - 1);
                int b = Mathf.Clamp(a + 1, 0, trilha.Count - 1);

                quadros.Add(Vector3.Lerp(trilha[a], trilha[b], passo - a));
            }

            return quadros;
        }

        static List<Vector3> Renderizar(List<Vector3> trilha, float atrasoMs, float perda,
            float snapshotHz, float bufferSnapshots)
        {
            var rng = new System.Random(Semente);

            float intervalo = 1f / snapshotHz;
            float duracao = trilha.Count * dt;

            // Snapshots que SOBREVIVERAM, com o instante de cada um.
            var tempos = new List<float>();
            var valores = new List<Vector3>();

            for (float t = 0f; t <= duracao; t += intervalo)
            {
                if (perda > 0f && rng.NextDouble() < perda)
                {
                    continue;
                }

                int passo = Mathf.Clamp(Mathf.RoundToInt(t / dt), 0, trilha.Count - 1);
                tempos.Add(t);
                valores.Add(trilha[passo]);
            }

            var quadros = new List<Vector3>();
            aquecimento = 0;
            float atraso = atrasoMs / 1000f;
            float atrasoTotal = atraso + bufferSnapshots * intervalo;

            for (float t = 0f; t <= duracao; t += 1f / RenderHz)
            {
                float alvo = t - atrasoTotal;

                // O cliente so pode usar snapshot que JA CHEGOU: o tirado no instante t_k do
                // host chega em t_k + atraso. Sem esta checagem o modelo interpola usando o
                // futuro, e entao nunca trava e nunca da salto — que era o efeito a flagrar.
                int ultimoRecebido = -1;
                for (int k = 0; k < tempos.Count; k++)
                {
                    if (tempos[k] + atraso <= t)
                    {
                        ultimoRecebido = k;
                    }
                    else
                    {
                        break;
                    }
                }

                // AQUECIMENTO: enquanto o cliente ainda nao recebeu dois snapshots uteis ele
                // so tem o que segurar, e isso nao e congelamento por perda — e a tubulacao
                // enchendo. Contar esses quadros inflaria `congelados` e o salto do primeiro
                // quadro util em proporcao ao RTT, que foi o que sujou a varredura anterior.
                if (ultimoRecebido < 1 || alvo <= tempos[0])
                {
                    aquecimento++;
                    continue;
                }

                int j = ultimoRecebido;
                while (j > 0 && tempos[j] > alvo)
                {
                    j--;
                }

                // O snapshot da frente nao chegou: o buffer secou. O cliente CONGELA no
                // ultimo que tem — e o salto aparece no quadro em que o atrasado finalmente
                // chega. E assim que perda de pacote vira teleporte na tela.
                if (j >= ultimoRecebido)
                {
                    quadros.Add(valores[ultimoRecebido]);
                    continue;
                }

                float span = tempos[j + 1] - tempos[j];
                float f = span <= 0f ? 0f : Mathf.Clamp01((alvo - tempos[j]) / span);
                quadros.Add(Vector3.Lerp(valores[j], valores[j + 1], f));
            }

            return quadros;
        }

        static void Publicar(string abordagem, List<Vector3> quadros, float inputMs, string nota)
        {
            var saltos = new List<float>(quadros.Count);

            for (int i = 1; i < quadros.Count; i++)
            {
                saltos.Add(Vector3.Distance(quadros[i], quadros[i - 1]));
            }

            saltos.Sort();

            float max = saltos.Count > 0 ? saltos[saltos.Count - 1] : 0f;
            float p99 = saltos.Count > 0
                ? saltos[Mathf.Clamp(Mathf.CeilToInt(0.99f * saltos.Count) - 1, 0, saltos.Count - 1)]
                : 0f;

            // A mediana vai junto de proposito: max e p99 lidos sozinhos passam por tipico,
            // e o tipico e o que diz se a viga anda lisa ou aos trancos.
            float mediana = saltos.Count > 0 ? saltos[saltos.Count / 2] : 0f;

            // Quadro congelado = o cliente redesenhou a viga exatamente onde ela estava.
            // E a outra metade do sintoma: travar e depois saltar.
            int congelados = 0;
            foreach (float s in saltos)
            {
                if (s <= 1e-6f)
                {
                    congelados++;
                }
            }

            bool reprova = max > CarryJumpLimitU || inputMs > 100f;

            Log(F("[MODELO] ABORD[{0}] quadros={1} aquecimento={2} carry_jump_max_u={3:F4} " +
                  "carry_jump_p99_u={4:F4} carry_jump_mediana_u={5:F4} congelados={6} " +
                  "input_ms={7:F1} reprova={8} — {9}",
                abordagem, quadros.Count, aquecimento, max, p99, mediana, congelados, inputMs,
                reprova ? 1 : 0, nota));
        }

        static void PublicarPrevisao(string abordagem, Trilha trilha, int passosMeioRtt)
        {
            if (trilha.Divergencias.Count == 0)
            {
                Fail("nenhum ponto de checagem da abordagem 2 foi gravado");
                return;
            }

            var d = new List<float>(trilha.Divergencias);
            d.Sort();

            var ctrl = new List<float>(trilha.DivergenciasOraculo);
            ctrl.Sort();

            float max = d[d.Count - 1];
            float p99 = d[Mathf.Clamp(Mathf.CeilToInt(0.99f * d.Count) - 1, 0, d.Count - 1)];
            float mediana = d[d.Count / 2];
            float ctrlMax = ctrl[ctrl.Count - 1];

            bool reprova = max > CarryJumpLimitU;

            Log(F("[MODELO] CONTROLE[{0}] oraculo_max_u={1:F6} oraculo_mediana_u={2:F6} — " +
                  "previsao com o input de TODOS conhecido; tem que ser ~0, senao o numero " +
                  "abaixo mede o erro do Salvar/Restaurar e nao o da abordagem",
                abordagem, ctrlMax, ctrl[ctrl.Count / 2]));

            Log(F("[MODELO] ABORD[{0}] checagens={1} correcao_max_u={2:F4} " +
                  "correcao_p99_u={3:F4} correcao_mediana_u={4:F4} input_ms=0.0 reprova={5} — " +
                  "salto = erro de reconciliacao apos {6} passos de previsao sem saber o " +
                  "input dos outros 3",
                abordagem, d.Count, max, p99, mediana, reprova ? 1 : 0, passosMeioRtt));
        }

        // ------------------------------------------------------------------ cena

        /// <summary>
        /// O roteiro de cada portador, por passo. Cada um tem o seu de propósito: quatro
        /// jogadores andando em lockstep sao o MELHOR caso possivel para previsao — o
        /// cliente acerta o palpite dos outros sem esforco —, e nada no briefing promete
        /// lockstep. Aqui um inverte cedo, um inverte tarde, e um para no meio do caminho.
        /// </summary>
        static MoveIntent IntentDe(int portador, int passo)
        {
            switch (portador)
            {
                case 0:
                    return Mover(passo < LegSteps ? 1f : -1f);
                case 1:
                    return Mover(passo < LegSteps * 0.6f ? 1f : -1f);
                case 2:
                    return Mover(passo < LegSteps * 1.45f ? 1f : -1f);
                default:
                    return passo >= LegSteps * 0.8f && passo < LegSteps * 1.6f
                        ? default
                        : Mover(1f);
            }
        }

        static MoveIntent Mover(float z)
        {
            return new MoveIntent { Move = new Vector2(0f, z) };
        }

        static void PassoRoteiro(List<GameObject> portadores, int passo)
        {
            for (int p = 0; p < portadores.Count; p++)
            {
                var motor = portadores[p].GetComponent<CapsuleMotor>();
                if (motor != null)
                {
                    motor.Step(IntentDe(p, passo), dt);
                }
            }
        }

        static void Passo(List<GameObject> portadores, MoveIntent intent)
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

        static List<GameObject> CriarPortadores(GameObject playerGo, GameObject beamGo, int quantos)
        {
            var lista = new List<GameObject>(quantos);
            float meia = CarryBeam.LengthU * 0.5f - 0.5f;

            for (int i = 0; i < quantos; i++)
            {
                float t = quantos <= 1 ? 0.5f : i / (float)(quantos - 1);

                var copia = Object.Instantiate(playerGo);
                copia.name = "Holder_" + i;
                copia.transform.position = beamGo.transform.position
                                           + new Vector3(Mathf.Lerp(-meia, meia, t), 0.75f, -0.9f);
                copia.transform.rotation = Quaternion.identity;

                var body = copia.GetComponent<Rigidbody>();
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;

                lista.Add(copia);
            }

            return lista;
        }

        static void Destruir(List<GameObject> portadores)
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

        static string F(string formato, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, formato, args);
        }

        static void Log(string mensagem)
        {
            Debug.Log("[REDE] " + mensagem);
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
