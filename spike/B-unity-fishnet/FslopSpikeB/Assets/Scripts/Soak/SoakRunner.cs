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

        /// <summary>
        /// RTT de ida-e-volta a injetar, em ms, e perda em porcento. Os dois do briefing:
        /// "sob 150ms RTT e 3% packet loss simulados". Zero = nao injeta nada, que e o que
        /// todas as corridas ate 18/09 fizeram.
        /// </summary>
        long rttMs;
        double lossPct;

        /// <summary>
        /// Ticks de interpolacao do NetworkTransform. 0 = nao mexe, fica o default da cena.
        ///
        /// E o botao que o docs/99 item 21 identificou e nao tinha medido. Ele nao controla
        /// so a suavidade: o descarte que FAZ a viga teleportar dispara quando a fila passa
        /// de `_interpolation + 3` (NetworkTransform.cs:2425), entao interpolacao maior
        /// tolera rajada maior — ao custo de mais atraso, que e a moeda que o item 19 ja nao
        /// tem. Medir os dois lados do mesmo dial e o ponto.
        /// </summary>
        int interpolacao;
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
        bool mundoCompleto;

        /// <summary>Transporte que conta bytes, ou nulo se a cena nao tiver um.</summary>
        ByteCountingTugboat contador;

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

            // O TimeManager pode sobreviver a este objeto na ordem de destruicao da cena.
            // Emitir [SOAK-POS] depois do shutdown sujaria a serie com ticks de um mundo
            // que ja parou.
            if (rede != null && rede.TimeManager != null)
            {
                rede.TimeManager.OnPostTick -= AoPassarOTickDaRede;
            }
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

            // A pilha NAO nasce aqui. `rede.IsServerStarted` ainda e false neste ponto: o
            // ServerManager.StartConnection de LigarRede() abre o socket numa thread e o
            // estado so vira Started no tick seguinte. Criar as caixas agora as faz nascer
            // fora da rede — 151 corpos no host, 1 no cliente, e NENHUM erro no log, porque
            // instanciar um prefab de rede sem spawnar e legitimo.
            //
            // E a mesma forma da changes/12: o que depende do servidor no ar tem que esperar
            // o servidor no ar. Nasce em ResolverMundoLocal, chamado do Update.
            pilha = FindAnyObjectByType<BoxStackSpawner>();

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
        bool ResolverMundoLocal()
        {
            var achada = FindAnyObjectByType<CarryBeam>();
            if (achada == null)
            {
                return false;
            }

            // A viga so aparece quando o FishNet registra objeto de cena, o que acontece
            // depois de o servidor subir. Ou seja: chegar aqui JA E a prova de que
            // IsServerStarted e true, e e por isso que a pilha nasce neste ponto — as 150
            // caixas precisam de servidor no ar para serem spawnadas pela rede.
            if (pilha != null)
            {
                sondas.AddRange(pilha.Spawn(rede));
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
        /// Quantos corpos o mundo tem, segundo o TESTE MINIMO — 150 caixas + a viga.
        ///
        /// O cliente sabe esse numero sem perguntar ao host porque ele carrega a mesma cena,
        /// com o mesmo BoxStackSpawner: o numero e do briefing, nao do host. E por isso que
        /// "estado completo" pode ser conferido no cliente em vez de depender de um campo
        /// que o host mandaria (e que, se mandasse, nao provaria nada — seria o host se
        /// auditando).
        /// </summary>
        int CorposEsperados()
        {
            var spawner = FindAnyObjectByType<BoxStackSpawner>();
            return (spawner == null ? 0 : spawner.PlannedCount) + 1;
        }

        /// <summary>
        /// Recolhe os corpos replicados que ja chegaram. Devolve true quando o mundo do
        /// cliente esta COMPLETO — e so ai o late join acabou.
        ///
        /// Antes da change 18 isto procurava so a viga e declarava late join feito no quadro
        /// em que ela aparecia. Com 150 caixas chegando depois, aquele instante deixou de
        /// ser "recebi o estado" e virou "recebi o primeiro objeto".
        /// </summary>
        bool ResolverMundoReplicado()
        {
            int esperados = CorposEsperados();

            // Recolhe tudo que ja existe. FindObjectsByType ignora inativos, e e exatamente
            // o que se quer: objeto de rede so fica ativo depois do spawn.
            sondas.Clear();
            foreach (var corpo in FindObjectsByType<Rigidbody>())
            {
                var nob = corpo.GetComponent<FishNet.Object.NetworkObject>();
                if (nob != null && nob.IsSpawned)
                {
                    corpo.isKinematic = true;
                    sondas.Add(corpo);
                }
            }

            if (viga == null)
            {
                ResolverVigaReplicada();
            }

            if (sondas.Count < esperados)
            {
                return false;
            }

            int ajustados = AplicarInterpolacao();

            Evento("late_join_done", string.Format(CultureInfo.InvariantCulture,
                "elapsed_ms={0:F1} ntick={1} world_hash={2} bodies={3} esperados={4} " +
                "interp={5} interp_aplicada_em={6}",
                (Decorrido() - inicioDoLateJoin) * 1000f, TickDaRede(), HashDoMundo(),
                sondas.Count, esperados, interpolacao, ajustados));

            return true;
        }

        /// <summary>
        /// Aplica `-soakInterp` a todos os NetworkTransform que ja existem. Devolve quantos.
        ///
        /// Roda DEPOIS de o mundo estar completo, e nao no Start, porque as 150 caixas sao
        /// spawnadas e so existem la. Devolver a contagem importa: uma corrida que pedisse
        /// interpolacao 6 e ajustasse 1 corpo em vez de 151 daria um numero sobre outra coisa,
        /// e sem esse campo ninguem saberia.
        ///
        /// Zero significa "nao mexe": o default da cena e um valor medido, e sobrescrever por
        /// acidente trocaria a linha de base de todas as corridas anteriores.
        /// </summary>
        int AplicarInterpolacao()
        {
            if (interpolacao <= 0)
            {
                return 0;
            }

            int ajustados = 0;
            foreach (var sync in
                     FindObjectsByType<FishNet.Component.Transforming.NetworkTransform>())
            {
                sync.SetInterpolation((ushort)interpolacao);
                ajustados++;
            }

            return ajustados;
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

            // Redundante desde que o NetworkTransform passou a saber do Rigidbody
            // (ConfigurarSincroniaDeRigidbody no gerador de cena): ele ja torna o corpo
            // kinematic no cliente. Fica porque e barato e porque nao depende de a
            // configuracao da cena estar certa — se ela se perder numa regeracao, a viga
            // ainda nao cai por gravidade local. O que NAO da para fazer daqui e desligar
            // `interpolation`: isso e o par obrigatorio do kinematic (errors/07) e quem
            // tem que fazer e a configuracao, no momento certo do ciclo de vida.
            viga.Body.isKinematic = true;

            // A viga NAO entra em `sondas` aqui: quem monta a lista e ResolverMundoReplicado,
            // que recolhe todos os corpos spawnados de uma vez. Adicionar aqui duplicaria
            // ela, e um corpo contado duas vezes faria `bodies` bater 151 com 150 corpos
            // reais — falso verde exatamente na metrica que este caminho serve.
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

            // Pode ser nulo: se a cena for regerada sem o ByteCountingTugboat, o
            // TransportManager adiciona um Tugboat comum sozinho (TransportManager.cs:267) e
            // a corrida roda igual — com rx/tx voltando a -1, que e o certo. O que NAO pode
            // acontecer e sair numero de banda sem contador por tras.
            contador = transporte as ByteCountingTugboat;

            ConfigurarRedeRuim();

            rede.ServerManager.OnRemoteConnectionState += AoMudarEstadoDoPar;
            rede.ServerManager.OnAuthenticationResult += AoAutenticar;
            rede.ClientManager.OnClientConnectionState += AoMudarEstadoDoCliente;
            rede.TimeManager.OnPostTick += AoPassarOTickDaRede;

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
        /// Injeta o RTT e a perda que o briefing manda simular: "sob 150ms RTT e 3% packet
        /// loss simulados".
        ///
        /// METADE do RTT vai para cada ponta. O simulador do FishNet aplica o valor
        /// configurado por pacote e por SENTIDO (LatencySimulator.cs:245-247, latencia em
        /// segundos = _latency/1000 somada a cada pacote de saida), e as duas instancias
        /// rodam o proprio simulador — logo ida + volta = 2x o valor.
        ///
        /// A ARMADILHA, e e por isso que existe a checagem no fim: o simulador e publico e
        /// aceita configuracao em qualquer build, mas os pontos que o CONSULTAM estao atras
        /// de `#if DEVELOPMENT`, que o TransportManager define como
        /// `UNITY_EDITOR || DEVELOPMENT_BUILD` (TransportManager.cs:1-3). Num build de
        /// release isto roda inteiro, sem erro, e nao tem efeito nenhum — uma corrida sairia
        /// com `rtt_ms=150` no cabecalho e rede perfeita no resultado. Numero publicado com
        /// condicao que nunca existiu e pior que numero nenhum.
        /// </summary>
        void ConfigurarRedeRuim()
        {
            if (rttMs <= 0 && lossPct <= 0d)
            {
                return;
            }

            var simulador = rede.TransportManager.LatencySimulator;
            simulador.SetLatency(rttMs / 2);
            simulador.SetPacketLoss(lossPct / 100d);
            simulador.SetEnabled(true);

            // Debug.isDebugBuild, e nao `#if DEVELOPMENT_BUILD`: o Unity 6 deprecou a
            // diretiva (UAC0009) e recomenda a checagem em runtime. Ela tambem e a pergunta
            // certa — o que importa nao e como este codigo foi compilado, e se o programa que
            // esta rodando AGORA e aquele em que o simulador do FishNet e consultado.
            if (Application.isEditor || Debug.isDebugBuild)
            {
                Evento("net_degradada", string.Format(CultureInfo.InvariantCulture,
                    "rtt_ms={0} por_sentido_ms={1} loss_pct={2:F1} ativo=1",
                    rttMs, rttMs / 2, lossPct));
            }
            else
            {
                Evento("exception", "where=LatencySimulator_inerte_em_build_de_release");
            }
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

        /// <summary>
        /// Emite a posicao da viga contra o RELOGIO DA REDE, uma linha por tick, nas duas
        /// pontas. E a unica forma de medir drift host x cliente: a comparacao NAO pode
        /// acontecer dentro de um processo so, porque nenhum dos dois conhece a verdade do
        /// outro. Quem cruza as duas series e o avaliador, depois da corrida, casando pelo
        /// ntick.
        ///
        /// Por que TimeManager.Tick e nao o `tick` local deste script: o `tick` daqui e um
        /// contador de FixedUpdate que comeca em zero quando o PROCESSO sobe, e os dois
        /// processos sobem em instantes diferentes — casar por ele compararia momentos
        /// diferentes da simulacao. O Tick do FishNet e o relogio do servidor, e o cliente
        /// o acompanha.
        ///
        /// RESSALVA que precisa sobreviver ate o relatorio, lida em TimeManager.cs:126: no
        /// cliente esse valor e uma APROXIMACAO do tick do servidor e "may increase and
        /// decrease as timing adjusts". O eixo da comparacao tem erro proprio. A 30 Hz
        /// (TickRate default, TimeManager.cs:184) um tick vale 33 ms; com a viga a ~0.5 u/s
        /// isso da ~0.017 u de erro de eixo, contra o limite de 0.15 u do briefing — uma
        /// ordem de grandeza abaixo, mas nao zero, e por isso fica escrito.
        /// </summary>
        void AoPassarOTickDaRede()
        {
            if (viga == null)
            {
                return;
            }

            uint ntick = rede.TimeManager.Tick;

            // A viga, todo tick: e o corpo que se move o tempo todo, e o unico que o
            // briefing cita nominalmente ("o objeto carregado nao teleporta").
            EmitirPosicao(ntick, viga.Body);

            // Os outros 150, a cada VarreduraDeCorpos ticks. A cadencia e contada em tick de
            // REDE e nao em segundos, de proposito: assim as duas pontas emitem exatamente
            // nos MESMOS ticks. Amostrando cada uma no proprio relogio, as series nao teriam
            // par nenhum para cruzar e o drift das caixas ficaria sem como ser calculado.
            if (ntick % VarreduraDeCorpos != 0)
            {
                return;
            }

            foreach (var corpo in sondas)
            {
                if (corpo != null && corpo != viga.Body)
                {
                    EmitirPosicao(ntick, corpo);
                }
            }
        }

        /// <summary>
        /// De quantos em quantos ticks de rede os 151 corpos sao varridos. 30, a 30 Hz, e uma
        /// vez por segundo: ~151 linhas/s por instancia, ~100 mil num soak de 10 min. Varrer
        /// todos a cada tick daria 4500 linhas/s e o log viraria o gargalo da medicao — o
        /// instrumento passaria a medir a si mesmo.
        /// </summary>
        const uint VarreduraDeCorpos = 30;

        /// <summary>
        /// Uma linha de posicao. O `t` entra porque o briefing mede drift "apos 5 min de
        /// simulacao continua" e so o ntick nao diz quantos segundos se passaram; vale o `t`
        /// do HOST, porque para um cliente que entrou atrasado o relogio local dele nao
        /// descreve ha quanto tempo o mundo simula.
        /// </summary>
        void EmitirPosicao(uint ntick, Rigidbody corpo)
        {
            // O ObjectId e o que identifica o MESMO corpo nas duas pontas. Nome nao serve: o
            // cliente recebe clones do prefab e o FishNet nao sincroniza nome. Posicao na
            // lista tambem nao: a ordem de chegada no cliente nao e a de criacao no host.
            var nob = corpo.GetComponent<FishNet.Object.NetworkObject>();
            if (nob == null)
            {
                return;
            }

            Vector3 p = corpo.transform.position;

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[SOAK-POS] ntick={0} t={1:F3} role={2} id={3} obj={4} x={5:F4} y={6:F4} z={7:F4}",
                ntick, Decorrido(), papel, identidade, nob.ObjectId, p.x, p.y, p.z));
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

                // A metrica "queda do host" do briefing: "encerra com mensagem limpa, sem
                // excecao nao tratada. Host migration NAO e requisito." Ou seja, o certo
                // aqui e SAIR, e sair dizendo por que — nao tentar reconectar, nao seguir
                // simulando um mundo que nao existe mais.
                //
                // O guarda do `encerrando` importa: quando o proprio cliente encerra por
                // duracao, o socket tambem para, e sem ele este caminho sobrescreveria o
                // motivo real do encerramento por `host_lost`.
                if (!SouServidor && !encerrando)
                {
                    Encerrar("host_lost");
                }
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
            // Os corpos de rede so existem depois que o FishNet os registra, nos DOIS papeis:
            // no cliente ate o spawn vindo do servidor, no host ate o servidor subir. Por
            // isso a busca fica aqui, e nao no Start.
            if (SouServidor)
            {
                if (viga == null)
                {
                    ResolverMundoLocal();
                }
            }
            else if (!mundoCompleto)
            {
                mundoCompleto = ResolverMundoReplicado();
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
            // build_flavor entrou porque o simulador de latencia do FishNet so e consultado
            // em development build (docs/99 item 20), e development build NAO e o mesmo
            // programa — sem stripping, com hooks de profiler. O fps de um nao vale para o
            // outro. Sem este campo, duas corridas incomparaveis pareceriam a mesma.
            Linha(string.Format(CultureInfo.InvariantCulture,
                "[SOAK-META] run={0} stack=B role={1} id={2} pid={3} build={4} " +
                "build_flavor={5} engine={6} transport=local display={7} rtt_ms={8} " +
                "loss_pct={9:F1} bodies={10} started={11}",
                run,
                papel,
                identidade,
                System.Diagnostics.Process.GetCurrentProcess().Id,
                build,
                Debug.isDebugBuild ? "development" : "release",
                Application.unityVersion,
                display,
                rttMs,
                lossPct,
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

            // rx/tx = -1 continua significando NAO INSTRUMENTADO, e nao "zero bytes" — e e
            // o que sai quando o transporte da cena nao e o que conta (docs/00). Quando e,
            // o numero vem do ByteCountingTugboat: bytes de PAYLOAD entregues ao transporte
            // e recebidos dele, SEM cabecalho UDP/IP, sem enquadramento e sem acks do
            // Tugboat. O valor real no fio e maior, e a ressalva viaja junto do numero.
            float rx = -1f;
            float tx = -1f;
            if (contador != null)
            {
                contador.TakeAndReset(out long enviados, out long recebidos);
                tx = enviados / 1024f / janela;
                rx = recebidos / 1024f / janela;
            }

            Linha(string.Format(CultureInfo.InvariantCulture,
                "[SOAK] t={0:F3} tick={1} ntick={2} role={3} id={4} fps={5:F1} " +
                "frame_p99_ms={6:F3} rx_KBps={7:F2} tx_KBps={8:F2} drift_max_u=-1 " +
                "drift_p99_u=-1 carry_jump_u={9:F4} input_ms_p99=-1 bodies_awake={10} " +
                "world_hash={11}",
                Decorrido(), tick, TickDaRede(), papel, identidade,
                quadrosNoSegundo / janela, p99, rx, tx, maiorSaltoDaViga, acordados,
                HashDoMundo()));
        }

        /// <summary>
        /// Tick da rede, ou -1 quando nao ha rede. Existe porque `tick` — o contador de
        /// FixedUpdate — NAO e comparavel entre instancias: ele comeca em zero quando o
        /// processo sobe, e dois processos sobem em instantes diferentes. Casar world_hash
        /// por ele comparava momentos diferentes da simulacao, e era por isso que a checagem
        /// de late join reprovava com "nenhuma amostra do host no tick=5": o cliente estava
        /// no quinto FixedUpdate DELE, e o host, naquele numero de tick, estava em t=0.1 s.
        ///
        /// -1 e "nao instrumentado" (docs/00), nunca zero: zero e um tick de rede valido.
        /// </summary>
        long TickDaRede()
        {
            return rede == null || rede.TimeManager == null ? -1L : rede.TimeManager.Tick;
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

        /// <summary>
        /// Encerramento limpo. O host anuncia `host_quit` ANTES do proprio shutdown: e o
        /// evento que o contrato usa para distinguir "o host saiu de proposito" de "o host
        /// caiu", e sem ele o avaliador nao tem como saber que a desconexao do cliente era
        /// esperada.
        /// </summary>
        void Encerrar(string motivo = "duracao_atingida")
        {
            if (encerrando)
            {
                return;
            }

            encerrando = true;

            if (SouServidor)
            {
                Evento("host_quit", "motivo=" + motivo);
            }
            else if (!mundoCompleto)
            {
                // A corrida acabou e o mundo do cliente nunca completou. Isto NAO e emitido
                // como late_join_done: "done" tem que significar done. Sem este evento o
                // avaliador so poderia dizer "nenhum ev=late_join_done", que e verdade e nao
                // informa nada — e a diferenca entre "chegaram 149 de 151" e "nao chegou
                // nada" muda completamente onde procurar.
                Evento("late_join_timeout", string.Format(CultureInfo.InvariantCulture,
                    "esperou_ms={0:F1} bodies={1} esperados={2}",
                    (Decorrido() - inicioDoLateJoin) * 1000f, sondas.Count, CorposEsperados()));
            }

            Evento("shutdown", string.Format(CultureInfo.InvariantCulture,
                "clean=1 reason={0} excecoes={1}", motivo, excecoes));

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

                    // RTT de IDA-E-VOLTA. O simulador do FishNet aplica o valor dele por
                    // pacote e por sentido (LatencySimulator.cs:245), e cada instancia roda
                    // o proprio simulador — entao o que se configura la e metade disto.
                    case "-soakRtt":
                        long.TryParse(args[i + 1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out rttMs);
                        rttMs = System.Math.Max(rttMs, 0);
                        break;

                    case "-soakLoss":
                        double.TryParse(args[i + 1], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out lossPct);
                        lossPct = System.Math.Max(lossPct, 0d);
                        break;

                    case "-soakInterp":
                        int.TryParse(args[i + 1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out interpolacao);
                        interpolacao = Mathf.Max(interpolacao, 0);
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
