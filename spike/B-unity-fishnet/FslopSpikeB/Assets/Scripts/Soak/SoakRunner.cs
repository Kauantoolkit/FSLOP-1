using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace Fslop.SpikeB
{
    /// <summary>
    /// Emite o contrato de docs/00-contrato-de-medicao.md a partir de uma corrida real do
    /// player. E o unico codigo desta stack que fala com o avaliador.
    ///
    /// REGRA DO CONTRATO QUE MANDA AQUI: "Nenhuma stack pode calcular o proprio PASS/FAIL
    /// nem suavizar serie". Este arquivo mede e escreve. Ele nao decide nada, nao descarta
    /// outlier, nao ignora aquecimento e nao tem media movel. Quem julga e tools/soak.
    ///
    /// O QUE ESTA CORRIDA NAO TEM: uma segunda instancia. Nao ha cliente, nao ha transporte
    /// e nenhum byte atravessa socket nenhum — por isso rx_KBps e tx_KBps saem 0.0, e os
    /// campos que o contrato marca como "so cliente" (drift, input_ms_p99) saem -1, que e a
    /// convencao que o proprio contrato ja usa para o host.
    /// </summary>
    public class SoakRunner : MonoBehaviour
    {
        /// <summary>
        /// Corpos-sonda do contrato: a viga mais as 150 caixas. Sao eles que entram no
        /// world_hash e em bodies_awake. Os portadores ficam de fora de propósito — eles
        /// sao comandados por script, entao a posicao deles nao prova nada sobre a
        /// simulacao; as caixas e a viga sim.
        /// </summary>
        readonly List<Rigidbody> sondas = new List<Rigidbody>();

        readonly List<GameObject> portadores = new List<GameObject>();

        readonly List<float> quadrosMs = new List<float>();

        CarryBeam viga;
        BoxStackSpawner pilha;

        string run = "sem-id";
        string build = "desconhecido";
        string display = "desconhecido";
        float duracaoAlvo = 30f;

        /// <summary>
        /// Passos de fisica por lado do quadrado de patrulha; 50 passos = 1 s = 4 u a 4 u/s.
        /// E o GATILHO DE CARGA do soak, que docs/99 item 12 registra como nao escolhido:
        /// com lado grande a viga carregada alcanca a pilha de caixas e a derruba; com lado
        /// pequeno os portadores giram longe dela. Fica como argumento, e nao como constante,
        /// justamente para a escolha ser feita medindo os dois em vez de eu decidir.
        /// </summary>
        int passosPorLado = 200;

        /// <summary>
        /// `host` roda o servidor e simula; `client` so conecta. Nao e host-mode do FishNet
        /// (servidor + cliente local no mesmo processo) DE PROPOSITO: no host-mode o trafego
        /// do jogador local nao passa por socket, e a medicao de banda do contrato ficaria
        /// misturando o que atravessa a rede com o que nao atravessa.
        /// </summary>
        string papel = "host";

        int identidade;
        ushort porta = 7770;
        string endereco = "127.0.0.1";

        NetworkManager rede;

        bool SouServidor => papel == "host";

        double inicio;
        int tick;

        int quadrosNoSegundo;
        float relogioDaAmostra;
        float maiorSaltoDaViga;
        Vector3 vigaNoQuadroAnterior;
        bool temQuadroAnterior;

        int excecoes;
        bool encerrando;
        float inicioDoLateJoin;

        void Awake()
        {
            // Sem stack trace nas linhas de log: o contrato manda uma linha por amostra, sem
            // quebra. O trace do Unity quebraria toda linha em varias.
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

            // Medir fps com vsync ligado mede o monitor, nao a simulacao. O briefing pede
            // "60fps estaveis", e so da para saber se sobra folga com o teto solto.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            // SEM ISTO O SOAK MENTE. O padrao do player e parar o loop quando a janela perde
            // o foco. A corrida de 600 s de 17/09 congelou aos 96 s por causa disso: o
            // relogio de parede chegou a 1632 s enquanto o tick parou em 4458 (89 s de
            // simulacao), e a linha de shutdown saiu com t=1632 sem nada ter sido medido no
            // meio. Um soak automatizado nao tem quem clique na janela.
            Application.runInBackground = true;

            LerArgumentos();

            Application.logMessageReceived += AoReceberLog;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= AoReceberLog;
        }

        void Start()
        {
            inicio = Time.realtimeSinceStartupAsDouble;

            EmitirMeta();
            LigarRede();

            // O mundo so nasce no servidor. O cliente NAO instancia caixas nem portadores:
            // se instanciasse, teria um mundo proprio parecido com o do host, e a medicao de
            // drift compararia duas simulacoes independentes em vez de uma replicada — o
            // numero sairia, e nao significaria nada.
            if (!SouServidor)
            {
                PrepararCliente();
                return;
            }

            pilha = FindAnyObjectByType<BoxStackSpawner>();
            if (pilha != null)
            {
                sondas.AddRange(pilha.Spawn());
            }

            // A viga NAO e procurada aqui, nem no host. Desde que ela virou objeto de cena
            // em rede valido (changes/12), o proprio FishNet a DESATIVA no Start dela —
            // NetworkObject.cs:584, TryStartDeactivation, que roda enquanto o servidor
            // ainda nao subiu. Quem a reativa e ServerObjects.cs:523, e isso acontece
            // depois deste Start. Procurar aqui devolvia null e o spawn_done saia com 150
            // corpos em vez de 151, sem nenhum erro no log. Resolvida em Update, nos dois
            // papeis; o spawn_done do host sai junto, para nunca descrever mundo incompleto.
        }

        /// <summary>
        /// O cliente nao simula: ele recebe. A viga existe na cena dele tambem (e objeto de
        /// cena em rede), e o Rigidbody dela PRECISA virar kinematic — senao o PhysX local
        /// puxa a viga para baixo enquanto o NetworkTransform a puxa para a posicao do
        /// servidor, e o que se mediria seria a briga entre os dois, nao a replicacao.
        ///
        /// A capsula do jogador e os portadores tambem ficam parados no cliente: eles nao
        /// sao replicados ainda, e deixa-los cair produziria movimento local que nao veio
        /// de lugar nenhum.
        /// </summary>
        void PrepararCliente()
        {
            int congelados = CongelarCorposLocais();

            // A viga NAO e procurada aqui. O FishNet mantem objeto de cena em rede
            // DESATIVADO no cliente ate o servidor mandar o spawn, e FindAnyObjectByType
            // ignora inativos — procurar no Start devolve null sempre. Ela e resolvida em
            // Update, e o tempo ate aparecer e justamente o que late_join_done mede.
            Evento("late_join_begin", string.Format(CultureInfo.InvariantCulture,
                "at_t={0:F3} corpos_congelados={1}", Decorrido(), congelados));

            inicioDoLateJoin = Decorrido();
        }

        /// <summary>
        /// Corpos locais do cliente viram kinematic. Sem isso o PhysX local puxa a viga para
        /// baixo enquanto o NetworkTransform a puxa para a posicao do servidor, e o que se
        /// mediria seria a briga entre os dois em vez da replicacao.
        /// </summary>
        int CongelarCorposLocais()
        {
            int congelados = 0;

            foreach (var corpo in FindObjectsByType<Rigidbody>())
            {
                if (!corpo.isKinematic)
                {
                    corpo.isKinematic = true;
                    congelados++;
                }
            }

            return congelados;
        }

        /// <summary>
        /// Tenta achar a viga no host. Ela nao esta pronta no Start: o FishNet desativa
        /// objeto de cena em rede enquanto o servidor nao subiu (NetworkObject.cs:584) e so
        /// a reativa ao registra-la (ServerObjects.cs:523). Devolve true no quadro em que
        /// ela aparece, e e so ai que o mundo do host esta completo — por isso o spawn_done
        /// sai daqui, e nao do Start.
        /// </summary>
        bool ResolverVigaLocal()
        {
            var achada = FindAnyObjectByType<CarryBeam>();
            if (achada == null)
            {
                return false;
            }

            viga = achada;

            // No host a viga simula: nada de kinematic. O briefing e explicito em "fisica
            // simula SO no host".
            sondas.Add(viga.Body);
            MontarPortadores();

            Evento("spawn_done", "bodies=" + sondas.Count);

            return true;
        }

        /// <summary>
        /// Tenta achar a viga replicada. Devolve true no quadro em que ela aparece.
        /// </summary>
        bool ResolverVigaReplicada()
        {
            var achada = FindAnyObjectByType<CarryBeam>();
            if (achada == null)
            {
                return false;
            }

            viga = achada;
            viga.Body.isKinematic = true;

            // A viga entra nas sondas do cliente: e o unico corpo replicado, entao e sobre
            // ela que world_hash e carry_jump_u do cliente falam. O hash do cliente NAO vai
            // bater com o do host enquanto as 150 caixas nao forem replicadas — o host
            // descreve 151 corpos e o cliente 1. O avaliador vai reprovar late_join por
            // causa disso, e vai estar CERTO: o estado do cliente nao esta completo.
            sondas.Add(viga.Body);

            Evento("late_join_done", string.Format(CultureInfo.InvariantCulture,
                "elapsed_ms={0:F1} world_hash={1} bodies={2}",
                (Decorrido() - inicioDoLateJoin) * 1000f, HashDoMundo(), sondas.Count));

            return true;
        }

        /// <summary>
        /// Sobe o FishNet com Tugboat (UDP local). O servidor escuta; o cliente conecta.
        /// </summary>
        void LigarRede()
        {
            rede = FindAnyObjectByType<NetworkManager>();
            if (rede == null)
            {
                Evento("exception", "where=NetworkManager_ausente_na_cena");
                return;
            }

            var transporte = rede.TransportManager.Transport;
            transporte.SetPort(porta);
            transporte.SetClientAddress(endereco);

            rede.ServerManager.OnRemoteConnectionState += AoMudarEstadoDoPar;
            rede.ServerManager.OnAuthenticationResult += AoAutenticar;
            rede.ClientManager.OnClientConnectionState += AoMudarEstadoDoCliente;

            if (SouServidor)
            {
                rede.ServerManager.StartConnection(porta);
            }
            else
            {
                rede.ClientManager.StartConnection(endereco, porta);
            }

            Evento("transport_up", string.Format(CultureInfo.InvariantCulture,
                "papel={0} transporte={1} endereco={2} porta={3}",
                papel, transporte.GetType().Name, endereco, porta));
        }

        /// <summary>
        /// Objeto de CENA em rede so vai para o cliente que for observador daquela cena, e
        /// o cliente nao entra nela sozinho: quem inscreve e o servidor, com
        /// SceneManager.AddConnectionToScene. Sem esta chamada o cliente conecta, autentica
        /// e nunca ve objeto nenhum — foi exatamente o que aconteceu na 1a corrida, com o
        /// cliente emitindo world_hash de conjunto vazio por 28 s.
        /// </summary>
        void AoAutenticar(NetworkConnection conexao, bool autenticado)
        {
            if (!autenticado)
            {
                return;
            }

            rede.SceneManager.AddConnectionToScene(conexao, gameObject.scene);
            Evento("peer_authenticated", string.Format(CultureInfo.InvariantCulture,
                "conn={0} cena={1}", conexao.ClientId, gameObject.scene.name));
        }

        /// <summary>Lado servidor: um par entrou ou saiu.</summary>
        void AoMudarEstadoDoPar(NetworkConnection conexao, RemoteConnectionStateArgs args)
        {
            Evento(args.ConnectionState == RemoteConnectionState.Started
                    ? "peer_connected"
                    : "peer_disconnected",
                "conn=" + conexao.ClientId);
        }

        /// <summary>Lado cliente: o proprio socket mudou de estado.</summary>
        void AoMudarEstadoDoCliente(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                Evento("peer_connected", "conn=eu");
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                Evento("peer_disconnected", "conn=eu");
            }
        }

        /// <summary>
        /// Quatro portadores agarram a viga e a carregam a corrida inteira. Sem isso
        /// carry_jump_u sairia 0.0 a corrida toda — um PASS obtido por ninguem estar
        /// carregando nada, que e o falso verde que o contrato manda evitar.
        /// </summary>
        void MontarPortadores()
        {
            var modelo = GameObject.Find("Player");
            if (modelo == null)
            {
                return;
            }

            // O Player da cena vira o portador 0. O CapsuleController le teclado, e num soak
            // automatizado nao existe teclado: quem comanda e o roteiro.
            DesligarTeclado(modelo);
            portadores.Add(modelo);

            float meia = CarryBeam.LengthU * 0.5f - 0.5f;
            Vector3 baseViga = viga.transform.position;

            modelo.transform.position = baseViga + new Vector3(-meia, 0.75f, -0.9f);

            for (int i = 1; i < 4; i++)
            {
                var copia = Instantiate(modelo);
                copia.name = "Holder_" + i;
                copia.transform.position = baseViga
                    + new Vector3(Mathf.Lerp(-meia, meia, i / 3f), 0.75f, -0.9f);

                DesligarTeclado(copia);
                portadores.Add(copia);
            }

            foreach (var portador in portadores)
            {
                var corpo = portador.GetComponent<Rigidbody>();
                if (corpo != null)
                {
                    viga.Grab(corpo, portador.transform.TransformPoint(viga.HandAnchor));
                }
            }

            Evento("grab", "by=0 holders=" + portadores.Count);
        }

        static void DesligarTeclado(GameObject alvo)
        {
            var controle = alvo.GetComponent<CapsuleController>();
            if (controle != null)
            {
                Destroy(controle);
            }
        }

        void FixedUpdate()
        {
            tick++;

            for (int i = 0; i < portadores.Count; i++)
            {
                var motor = portadores[i].GetComponent<CapsuleMotor>();
                if (motor != null)
                {
                    motor.Step(Roteiro(i, tick), Time.fixedDeltaTime);
                }
            }
        }

        /// <summary>
        /// Roteiro de marcha: um quadrado de 4 s por lado, com cada portador defasado. A
        /// defasagem existe para a viga ser DISPUTADA — quatro capsulas em lockstep sao o
        /// caso facil, e changes/08 mostrou que e justamente onde o numero fica bonito por
        /// motivo errado.
        /// </summary>
        MoveIntent Roteiro(int portador, int passo)
        {
            int ciclo = passosPorLado;
            int fase = (passo + portador * 17) % (ciclo * 4);
            int lado = fase / ciclo;

            switch (lado)
            {
                case 0: return new MoveIntent { Move = new Vector2(0f, 1f) };
                case 1: return new MoveIntent { Move = new Vector2(1f, 0f) };
                case 2: return new MoveIntent { Move = new Vector2(0f, -1f) };
                default: return new MoveIntent { Move = new Vector2(-1f, 0f) };
            }
        }

        void Update()
        {
            // A viga esta desativada na cena ate o FishNet registra-la, nos DOIS papeis: no
            // cliente ate o spawn vindo do servidor, no host ate o servidor subir. Por isso
            // a busca fica aqui, e nao no Start.
            if (viga == null)
            {
                if (SouServidor)
                {
                    ResolverVigaLocal();
                }
                else
                {
                    ResolverVigaReplicada();
                }
            }

            float dtMs = Time.unscaledDeltaTime * 1000f;
            quadrosMs.Add(dtMs);
            quadrosNoSegundo++;

            // carry_jump_u do contrato: maior salto da viga entre dois quadros CONSECUTIVOS.
            // Entre quadros, nao entre passos de fisica — e o que se ve na tela.
            if (viga != null)
            {
                Vector3 agora = viga.transform.position;
                if (temQuadroAnterior)
                {
                    maiorSaltoDaViga = Mathf.Max(maiorSaltoDaViga,
                        Vector3.Distance(agora, vigaNoQuadroAnterior));
                }

                vigaNoQuadroAnterior = agora;
                temQuadroAnterior = true;
            }

            relogioDaAmostra += Time.unscaledDeltaTime;
            if (relogioDaAmostra >= 1f)
            {
                EmitirAmostra();
                relogioDaAmostra = 0f;
                quadrosNoSegundo = 0;
                maiorSaltoDaViga = 0f;
                quadrosMs.Clear();
            }

            if (!encerrando && Decorrido() >= duracaoAlvo)
            {
                Encerrar();
            }
        }

        float Decorrido()
        {
            return (float)(Time.realtimeSinceStartupAsDouble - inicio);
        }

        void EmitirMeta()
        {
            // display= nao estava no contrato original. Entrou porque docs/99 item 4 ja
            // registrava que medicao de fps sem dizer se foi no monitor local ou por sessao
            // remota nao e reprodutivel nesta maquina, que tem adaptador virtual do Parsec.
            Linha(string.Format(CultureInfo.InvariantCulture,
                "[SOAK-META] run={0} stack=B role={1} id={2} pid={3} build={4} engine={5} " +
                "transport=local display={6} rtt_ms=0 loss_pct=0.0 bodies={7} started={8}",
                run,
                papel,
                identidade,
                System.Diagnostics.Process.GetCurrentProcess().Id,
                build,
                Application.unityVersion,
                display,
                SouServidor ? EsperadoDeCorpos() : 0,
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
        }

        int EsperadoDeCorpos()
        {
            var spawner = FindAnyObjectByType<BoxStackSpawner>();
            return (spawner != null ? spawner.PlannedCount : 0) + 1;
        }

        void EmitirAmostra()
        {
            quadrosMs.Sort();

            float p99 = quadrosMs.Count > 0
                ? quadrosMs[Mathf.Clamp(Mathf.CeilToInt(0.99f * quadrosMs.Count) - 1,
                    0, quadrosMs.Count - 1)]
                : 0f;

            int acordados = 0;
            foreach (var corpo in sondas)
            {
                if (corpo != null && !corpo.IsSleeping())
                {
                    acordados++;
                }
            }

            // fps e "quadros no ultimo segundo", mas a janela real nem sempre fecha em 1.000 s
            // — o primeiro quadro do player levou 2.5 s, e dividir por 1 s fixo publicava
            // "fps=1.0" para uma janela de 2.5 s. Divide-se pela janela que de fato passou.
            float janela = Mathf.Max(relogioDaAmostra, 1e-4f);

            // rx/tx = -1 significa NAO INSTRUMENTADO, e nao "zero bytes". O FishNet 4.7.3 nao
            // expoe contagem de bytes de socket: NetworkTrafficStatistics.cs inteiro esta sob
            // `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, e as classes que guardam os contadores
            // (NetworkTraffic, e os campos Inbound/Outbound de BidirectionalNetworkTraffic)
            // sao `internal` ao assembly FishNet.Runtime. Medir banda exige um Transport
            // decorador que conte na passagem — e isso e change propria, nao um campo que eu
            // possa preencher com palpite.
            Linha(string.Format(CultureInfo.InvariantCulture,
                "[SOAK] t={0:F3} tick={1} role={2} id={3} fps={4:F1} frame_p99_ms={5:F3} " +
                "rx_KBps=-1 tx_KBps=-1 drift_max_u=-1 drift_p99_u=-1 " +
                "carry_jump_u={6:F4} input_ms_p99=-1 bodies_awake={7} world_hash={8}",
                Decorrido(), tick, papel, identidade, quadrosNoSegundo / janela, p99,
                maiorSaltoDaViga, acordados, HashDoMundo()));
        }

        /// <summary>
        /// world_hash do contrato: posicoes dos corpos-sonda quantizadas a 0.01 u, em FNV-1a
        /// de 32 bits. Quantizar e o que permite comparar instancias — float nao bate bit a
        /// bit entre maquinas, e 0.01 u e uma ordem de grandeza abaixo do limiar de drift.
        /// </summary>
        string HashDoMundo()
        {
            unchecked
            {
                uint h = 2166136261u;

                foreach (var corpo in sondas)
                {
                    if (corpo == null)
                    {
                        continue;
                    }

                    Vector3 p = corpo.position;
                    h = Misturar(h, Mathf.RoundToInt(p.x * 100f));
                    h = Misturar(h, Mathf.RoundToInt(p.y * 100f));
                    h = Misturar(h, Mathf.RoundToInt(p.z * 100f));
                }

                return h.ToString("x8", CultureInfo.InvariantCulture);
            }
        }

        static uint Misturar(uint h, int valor)
        {
            unchecked
            {
                uint v = (uint)valor;

                for (int i = 0; i < 4; i++)
                {
                    h ^= (v >> (i * 8)) & 0xFF;
                    h *= 16777619u;
                }

                return h;
            }
        }

        void Encerrar()
        {
            encerrando = true;
            Evento("shutdown", "clean=1 reason=duracao_atingida excecoes=" + excecoes);
            Application.Quit(0);
        }

        void Evento(string nome, string extras)
        {
            Linha(string.Format(CultureInfo.InvariantCulture,
                "[SOAK-EV] t={0:F3} tick={1} role={2} id={3} ev={4} {5}",
                Decorrido(), tick, papel, identidade, nome, extras));
        }

        /// <summary>
        /// Uma unica linha ev=exception reprova a corrida inteira, e e de proposito: o log
        /// tem que DIZER que houve excecao, em vez de o avaliador ter que adivinhar por
        /// regex sobre stack trace de duas engines diferentes.
        /// </summary>
        void AoReceberLog(string mensagem, string trace, LogType tipo)
        {
            if (tipo != LogType.Exception && tipo != LogType.Error)
            {
                return;
            }

            excecoes++;

            var onde = new StringBuilder(mensagem);
            onde.Replace('\n', ' ').Replace('\r', ' ');

            Linha(string.Format(CultureInfo.InvariantCulture,
                "[SOAK-EV] t={0:F3} tick={1} role={2} id={3} ev=exception where={4}",
                Decorrido(), tick, papel, identidade, onde.ToString()));
        }

        void LerArgumentos()
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "-soakRun":
                        run = args[i + 1];
                        break;
                    case "-soakBuild":
                        build = args[i + 1];
                        break;
                    case "-soakDisplay":
                        display = args[i + 1];
                        break;
                    case "-soakSeconds":
                        float.TryParse(args[i + 1], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out duracaoAlvo);
                        break;
                    case "-soakRole":
                        papel = args[i + 1] == "client" ? "client" : "host";
                        break;
                    case "-soakId":
                        int.TryParse(args[i + 1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out identidade);
                        break;
                    case "-soakPort":
                        ushort.TryParse(args[i + 1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out porta);
                        break;
                    case "-soakAddress":
                        endereco = args[i + 1];
                        break;
                    case "-soakPatrolSteps":
                        int.TryParse(args[i + 1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out passosPorLado);
                        passosPorLado = Mathf.Max(passosPorLado, 1);
                        break;
                }
            }
        }

        static void Linha(string texto)
        {
            Debug.Log(texto);
        }
    }
}
