using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fslop.SpikeB.EditorTools
{
    /// <summary>
    /// Abre a cena gerada e simula fisica em passos manuais, FORA do play mode, para
    /// transformar "a cena existe" em numero.
    ///
    /// Existe porque compilar e sintaxe: a change 02 provou que a stack compila e nao
    /// provou que nada se move. Esta sonda e o primeiro instrumento que observa
    /// comportamento na candidata B, e e proposital que ela tenha vindo antes do
    /// controle da capsula — instrumento primeiro, teste depois (decisions/05).
    ///
    /// Duas coisas que ela NAO pode fazer, as duas aprendidas apanhando (errors/01 e 02):
    /// salvar a cena (a simulacao move os transforms) e sair do processo com o
    /// Physics.simulationMode trocado (e ajuste GLOBAL do projeto, gravado em
    /// ProjectSettings/DynamicsManager.asset).
    ///
    /// Rodar: Unity.exe -projectPath ... -batchmode -quit -nographics
    ///        -executeMethod Fslop.SpikeB.EditorTools.SpikePhysicsProbe.Run
    /// </summary>
    public static class SpikePhysicsProbe
    {
        // --- fase 1, queda ---
        const int SettleMaxSteps = 600;          // 12 s a 50 Hz; a queda teorica leva ~1.1 s
        const float RestSpeedThreshold = 0.01f;  // u/s
        const int RestStepsRequired = 10;        // parado por 10 passos seguidos = repouso
        const float ExpectedRestY = 1.0f;        // meia-altura da capsula, topo do chao em y=0
        const float RestToleranceU = 0.02f;

        // --- fase 2, marcha ---
        const int MarchSteps = 100;              // 2 s
        const float ExpectedSpeed = 4.0f;        // o mesmo 4 u/s ancorado no contrato de medicao
        const float SpeedToleranceUps = 0.05f;

        // --- fase 3, pulo ---
        const int JumpMaxSteps = 200;            // 4 s
        const float ExpectedJumpHeightU = 1.2f;  // o jumpHeight do motor, que e PLACEHOLDER
        const float JumpToleranceFraction = 0.15f;

        // --- fase 4, pilha de 150 ---
        const int StackMaxSteps = 1500;          // 30 s a 50 Hz
        const float EscapeY = -0.5f;             // abaixo disto a caixa atravessou o chao

        /// <summary>
        /// Quanto a caixa mais deslocada pode andar durante o assentamento e a pilha ainda
        /// contar como "de pe". Meia caixa: acima disso ela nao acomodou, ela caiu.
        /// </summary>
        const float SelfCollapseU = 0.5f;

        // --- fase 5, desabamento ---
        // 280 N*s e exatamente o momento que a capsula de 70 kg carrega a 4 u/s. Nao e um
        // numero escolhido para funcionar: e o empurrao que UM JOGADOR consegue dar, e o
        // ponto e descobrir se ele derruba a pilha. Quem escolhe a caixa e a direcao e o
        // BoxStackSpawner, porque isso depende da forma da pilha.
        const float CollapseImpulse = 280f;

        static GameObject player;
        static Rigidbody body;
        static CapsuleMotor motor;
        static float dt;
        static string failure;

        public static void Run()
        {
            failure = null;

            // Lido e registrado ANTES de qualquer coisa: se algum dia esta linha aparecer
            // com "Script", uma corrida anterior vazou estado e o projeto esta sujo.
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

            // O Exit fica FORA do try de proposito: EditorApplication.Exit encerra o
            // processo na hora e nao desenrola finally nenhum.
            if (failure != null)
            {
                Debug.LogError("[PROBE] FAIL " + failure);
                EditorApplication.Exit(1);
                return;
            }

            Log("PASS");
            EditorApplication.Exit(0);
        }

        static void Execute()
        {
            var scene = EditorSceneManager.OpenScene(SpikeSceneBuilder.ScenePath, OpenSceneMode.Single);
            Log("cena=" + SpikeSceneBuilder.ScenePath + " raizes=" + scene.rootCount);

            player = Find(scene, SpikeSceneBuilder.PlayerName);
            var ground = Find(scene, SpikeSceneBuilder.GroundName);
            var camera = Find(scene, SpikeSceneBuilder.CameraName);
            var sun = Find(scene, SpikeSceneBuilder.SunName);

            if (failure != null)
            {
                return;
            }

            body = player.GetComponent<Rigidbody>();
            motor = player.GetComponent<CapsuleMotor>();

            if (body == null)
            {
                Fail("o objeto " + SpikeSceneBuilder.PlayerName + " nao tem Rigidbody");
                return;
            }

            if (motor == null)
            {
                Fail("o objeto " + SpikeSceneBuilder.PlayerName + " nao tem CapsuleMotor");
                return;
            }

            float groundTopY = ground.transform.position.y + ground.transform.localScale.y * 0.5f;
            dt = Time.fixedDeltaTime;

            Log(F("dt={0:F4} gravity_y={1:F3} mass_kg={2:F1} spawn_y={3:F4} ground_top_y={4:F4}",
                dt, Physics.gravity.y, body.mass, player.transform.position.y, groundTopY));

            // O atrito entra no log porque foi ele que reprovou a primeira corrida desta
            // sonda (errors/01): sem esta linha, um numero de velocidade nao diz contra
            // que material foi medido.
            var pmat = player.GetComponent<CapsuleCollider>().sharedMaterial;
            Log(pmat == null
                ? "material do player=NENHUM (usa o default do PhysX, atrito 0.6)"
                : F("material do player={0} atrito_din={1:F3} atrito_est={2:F3} combine={3}",
                    pmat.name, pmat.dynamicFriction, pmat.staticFriction, pmat.frictionCombine));

            Log(F("camera={0} luz={1} controle={2}",
                camera.GetComponent<Camera>() != null ? "ok" : "SEM Camera",
                sun.GetComponent<Light>() != null ? "ok" : "SEM Light",
                player.GetComponent<CapsuleController>() != null ? "ok" : "SEM CapsuleController"));

            Physics.simulationMode = SimulationMode.Script;

            float restY;
            if (!Settle(out restY))
            {
                return;
            }

            if (!March(restY))
            {
                return;
            }

            if (!Jump(restY))
            {
                return;
            }

            Stack(scene);
        }

        /// <summary>Fase 1: a capsula cai do spawn e para. Sem intent nenhuma.</summary>
        static bool Settle(out float restY)
        {
            restY = 0f;

            int restStreak = 0;
            float minY = float.MaxValue;
            int steps;

            for (steps = 1; steps <= SettleMaxSteps; steps++)
            {
                motor.Step(default, dt);
                Physics.Simulate(dt);

                float y = player.transform.position.y;
                float speed = body.linearVelocity.magnitude;
                if (y < minY)
                {
                    minY = y;
                }

                if (steps <= 3 || steps % 25 == 0)
                {
                    Log(F("queda step={0,3} t={1:F3} y={2:F4} vy={3:F4} grounded={4}",
                        steps, steps * dt, y, body.linearVelocity.y, motor.IsGrounded ? 1 : 0));
                }

                restStreak = speed < RestSpeedThreshold ? restStreak + 1 : 0;
                if (restStreak >= RestStepsRequired)
                {
                    break;
                }
            }

            if (restStreak < RestStepsRequired)
            {
                Fail(F("a capsula nao chegou ao repouso em {0} passos ({1:F2} s); y={2:F4} speed={3:F4}",
                    SettleMaxSteps, SettleMaxSteps * dt, player.transform.position.y,
                    body.linearVelocity.magnitude));
                return false;
            }

            restY = player.transform.position.y;
            float erro = Mathf.Abs(restY - ExpectedRestY);

            Log(F("REPOUSO passos={0} t={1:F3} y={2:F4} esperado={3:F4} erro_u={4:F4} " +
                  "penetracao_u={5:F4} min_y={6:F4} grounded={7}",
                steps, steps * dt, restY, ExpectedRestY, erro, ExpectedRestY - minY, minY,
                motor.IsGrounded ? 1 : 0));

            if (erro > RestToleranceU)
            {
                Fail(F("repouso fora da tolerancia: erro_u={0:F4} > {1:F4}", erro, RestToleranceU));
                return false;
            }

            if (!motor.IsGrounded)
            {
                Fail("a capsula esta em repouso sobre o chao e o motor nao a considera grounded");
                return false;
            }

            return true;
        }

        /// <summary>Fase 2: andar para o norte por MarchSteps passos com intent constante.</summary>
        static bool March(float restY)
        {
            var intent = new MoveIntent { Move = new Vector2(0f, 1f) };

            Vector3 inicio = player.transform.position;
            float maxDesvioY = 0f;

            for (int steps = 1; steps <= MarchSteps; steps++)
            {
                motor.Step(intent, dt);
                Physics.Simulate(dt);

                float desvio = Mathf.Abs(player.transform.position.y - restY);
                if (desvio > maxDesvioY)
                {
                    maxDesvioY = desvio;
                }

                if (steps == 1 || steps == 5 || steps % 25 == 0)
                {
                    Vector3 v = body.linearVelocity;
                    Log(F("marcha step={0,3} t={1:F3} z={2:F4} vz={3:F4} vy={4:F4} grounded={5}",
                        steps, steps * dt, player.transform.position.z, v.z, v.y,
                        motor.IsGrounded ? 1 : 0));
                }
            }

            Vector3 fim = player.transform.position;
            float distancia = new Vector2(fim.x - inicio.x, fim.z - inicio.z).magnitude;
            Vector3 vFinal = body.linearVelocity;
            float speedFinal = new Vector2(vFinal.x, vFinal.z).magnitude;
            float erroSpeed = Mathf.Abs(speedFinal - ExpectedSpeed);

            Log(F("MARCHA passos={0} t={1:F3} dist_u={2:F4} speed_final={3:F4} esperado={4:F4} " +
                  "erro_ups={5:F4} desvio_y_max_u={6:F4} drift_x_u={7:F4}",
                MarchSteps, MarchSteps * dt, distancia, speedFinal, ExpectedSpeed, erroSpeed,
                maxDesvioY, Mathf.Abs(fim.x - inicio.x)));

            if (erroSpeed > SpeedToleranceUps)
            {
                Fail(F("velocidade de marcha fora da tolerancia: erro_ups={0:F4} > {1:F4}",
                    erroSpeed, SpeedToleranceUps));
                return false;
            }

            return true;
        }

        /// <summary>Fase 3: parar, pular uma vez, medir o apice e a volta ao chao.</summary>
        static bool Jump(float restY)
        {
            // Frear antes: pular andando mistura duas medicoes numa so.
            for (int i = 0; i < 25; i++)
            {
                motor.Step(default, dt);
                Physics.Simulate(dt);
            }

            float yAntes = player.transform.position.y;
            float apice = yAntes;
            int stepsApice = 0;
            int stepsPouso = 0;
            bool subiu = false;

            for (int steps = 1; steps <= JumpMaxSteps; steps++)
            {
                var intent = new MoveIntent { JumpQueued = steps == 1 };
                motor.Step(intent, dt);
                Physics.Simulate(dt);

                float y = player.transform.position.y;
                if (y > apice)
                {
                    apice = y;
                    stepsApice = steps;
                }

                if (y > yAntes + 0.05f)
                {
                    subiu = true;
                }

                if (steps <= 3 || steps % 10 == 0)
                {
                    Log(F("pulo step={0,3} t={1:F3} y={2:F4} vy={3:F4} grounded={4}",
                        steps, steps * dt, y, body.linearVelocity.y, motor.IsGrounded ? 1 : 0));
                }

                if (subiu && stepsPouso == 0 && steps > stepsApice
                    && Mathf.Abs(y - yAntes) < RestToleranceU
                    && Mathf.Abs(body.linearVelocity.y) < RestSpeedThreshold)
                {
                    stepsPouso = steps;
                    break;
                }
            }

            float altura = apice - yAntes;
            float erroRelativo = Mathf.Abs(altura - ExpectedJumpHeightU) / ExpectedJumpHeightU;

            Log(F("PULO y_antes={0:F4} apice_y={1:F4} altura_u={2:F4} alvo_u={3:F4} " +
                  "erro_rel={4:F4} t_subida={5:F3} t_pouso={6:F3} y_final={7:F4}",
                yAntes, apice, altura, ExpectedJumpHeightU, erroRelativo,
                stepsApice * dt, stepsPouso * dt, player.transform.position.y));

            if (!subiu)
            {
                Fail("a capsula nao saiu do chao com JumpQueued");
                return false;
            }

            if (erroRelativo > JumpToleranceFraction)
            {
                Fail(F("altura de pulo fora da tolerancia: erro_rel={0:F4} > {1:F4}",
                    erroRelativo, JumpToleranceFraction));
                return false;
            }

            if (stepsPouso == 0)
            {
                Fail(F("a capsula nao voltou ao repouso em {0} passos depois do pulo", JumpMaxSteps));
                return false;
            }

            if (Mathf.Abs(player.transform.position.y - restY) > RestToleranceU)
            {
                Fail(F("pousou fora do repouso: y={0:F4} contra {1:F4}",
                    player.transform.position.y, restY));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Fase 4: as 150 caixas 1x1 do teste minimo. Mede tres coisas que a change 09 vai
        /// precisar antes de existir fps: quanto custa um passo de fisica com 150 corpos,
        /// quantos passos a pilha leva para dormir, e se ela se mantem inteira.
        ///
        /// O custo por passo NAO e fps e nao pode ser publicado como tal: aqui nao ha
        /// render, nem scripts de gameplay, nem rede. E o piso do orcamento de 16.67 ms,
        /// nao o gasto.
        /// </summary>
        static bool Stack(Scene scene)
        {
            var host = Find(scene, SpikeSceneBuilder.BoxStackName);
            if (failure != null)
            {
                return false;
            }

            var spawner = host.GetComponent<BoxStackSpawner>();
            if (spawner == null)
            {
                Fail("o objeto " + SpikeSceneBuilder.BoxStackName + " nao tem BoxStackSpawner");
                return false;
            }

            var caixas = spawner.Spawn();
            var origem = new Vector3[caixas.Count];
            for (int i = 0; i < caixas.Count; i++)
            {
                origem[i] = caixas[i].transform.position;
            }

            Log(F("PILHA criada corpos={0} planejados={1} massa_kg={2:F1} origem_y_topo={3:F4}",
                caixas.Count, spawner.PlannedCount, caixas[0].mass,
                origem[caixas.Count - 1].y));

            if (caixas.Count != spawner.PlannedCount)
            {
                Fail(F("a pilha nasceu com {0} corpos e o plano era {1}",
                    caixas.Count, spawner.PlannedCount));
                return false;
            }

            var custos = new List<double>(StackMaxSteps);
            var cronometro = new System.Diagnostics.Stopwatch();

            int passoDormiu = 0;
            int acordados = caixas.Count;
            int steps;

            for (steps = 1; steps <= StackMaxSteps; steps++)
            {
                cronometro.Restart();
                Physics.Simulate(dt);
                cronometro.Stop();
                custos.Add(cronometro.Elapsed.TotalMilliseconds);

                acordados = 0;
                for (int i = 0; i < caixas.Count; i++)
                {
                    if (!caixas[i].IsSleeping())
                    {
                        acordados++;
                    }
                }

                if (steps <= 3 || steps % 100 == 0)
                {
                    Log(F("pilha step={0,4} t={1:F3} acordados={2,3} custo_ms={3:F3}",
                        steps, steps * dt, acordados, custos[custos.Count - 1]));
                }

                if (acordados == 0)
                {
                    passoDormiu = steps;
                    break;
                }
            }

            float maxDesloc = 0f;
            int fugitivas = 0;
            for (int i = 0; i < caixas.Count; i++)
            {
                Vector3 agora = caixas[i].transform.position;
                float d = Vector3.Distance(agora, origem[i]);
                if (d > maxDesloc)
                {
                    maxDesloc = d;
                }

                if (agora.y < EscapeY)
                {
                    fugitivas++;
                }
            }

            Log(F("PILHA passos={0} t={1:F3} dormiu_no_passo={2} acordados_final={3} " +
                  "custo_ms_1o={4:F3} custo_ms_medio={5:F3} custo_ms_p99={6:F3} custo_ms_max={7:F3} " +
                  "max_desloc_u={8:F4} fugitivas={9}",
                steps, steps * dt, passoDormiu, acordados,
                custos[0], Media(custos), Percentil(custos, 0.99), Maximo(custos),
                maxDesloc, fugitivas));

            if (fugitivas > 0)
            {
                Fail(F("{0} caixas atravessaram o chao (y < {1:F2})", fugitivas, EscapeY));
                return false;
            }

            if (passoDormiu == 0)
            {
                Fail(F("a pilha nao dormiu em {0} passos ({1:F1} s); ainda {2} acordados",
                    StackMaxSteps, StackMaxSteps * dt, acordados));
                return false;
            }

            // Esta checagem existe porque eu li o desabamento espontaneo A OLHO num log
            // que a sonda tinha marcado PASS (errors/03). O instrumento tem que pegar isso,
            // nao eu: uma pilha que cai sozinha nao serve de estado inicial para medir nada,
            // porque a corrida ja comeca com o pior caso gasto.
            if (maxDesloc > SelfCollapseU)
            {
                Fail(F("a pilha desabou sozinha antes de qualquer empurrao: " +
                       "max_desloc_u={0:F4} > {1:F4}", maxDesloc, SelfCollapseU));
                return false;
            }

            return Collapse(spawner, caixas, origem);
        }

        /// <summary>
        /// Fase 5: derrubar a pilha e medir o pior caso.
        ///
        /// A pilha assentada dorme e nao custa quase nada — e por isso o numero dela nao
        /// serve para orcar 60 fps. O que orca e a pilha DESABANDO, que e tambem o momento
        /// em que a banda por cliente estoura (o contrato de medicao diz isso na coluna
        /// bodies_awake). Esta fase produz esse pior caso de forma deterministica.
        /// </summary>
        static bool Collapse(BoxStackSpawner spawner, List<Rigidbody> caixas, Vector3[] origem)
        {
            // As posicoes de ANTES do empurrao. Sem elas nao da para saber o que o
            // empurrao fez: medir contra o nascimento da pilha soma o que ela ja tinha
            // andado assentando, e foi assim que uma corrida marcou "131 caixas movidas"
            // sem o empurrao ter movido nenhuma (errors/03).
            var antes = new Vector3[caixas.Count];
            for (int i = 0; i < caixas.Count; i++)
            {
                antes[i] = caixas[i].transform.position;
            }

            var alvo = caixas[spawner.ShoveTargetIndex()];
            alvo.WakeUp();
            alvo.AddForce(spawner.ShoveDirection * CollapseImpulse, ForceMode.Impulse);

            Log(F("DESABAMENTO impulso={0:F1} Ns em {1} (indice {2}) massa={3:F1} kg " +
                  "pos=({4:F2},{5:F2},{6:F2}) massa_total_pilha={7:F0} kg",
                CollapseImpulse, alvo.name, spawner.ShoveTargetIndex(), alvo.mass,
                alvo.transform.position.x, alvo.transform.position.y, alvo.transform.position.z,
                alvo.mass * caixas.Count));

            var custos = new List<double>(StackMaxSteps);
            var cronometro = new System.Diagnostics.Stopwatch();

            int picoAcordados = 0;
            int passoPico = 0;
            int passoDormiu = 0;
            int acordados = 0;
            int steps;

            for (steps = 1; steps <= StackMaxSteps; steps++)
            {
                cronometro.Restart();
                Physics.Simulate(dt);
                cronometro.Stop();
                custos.Add(cronometro.Elapsed.TotalMilliseconds);

                acordados = 0;
                for (int i = 0; i < caixas.Count; i++)
                {
                    if (!caixas[i].IsSleeping())
                    {
                        acordados++;
                    }
                }

                if (acordados > picoAcordados)
                {
                    picoAcordados = acordados;
                    passoPico = steps;
                }

                if (steps <= 3 || steps % 100 == 0)
                {
                    Log(F("desab step={0,4} t={1:F3} acordados={2,3} custo_ms={3:F3}",
                        steps, steps * dt, acordados, custos[custos.Count - 1]));
                }

                if (acordados == 0)
                {
                    passoDormiu = steps;
                    break;
                }
            }

            float maxDeslocTotal = 0f;
            float maxDeslocEmpurrao = 0f;
            int fugitivas = 0;
            int movidas = 0;
            for (int i = 0; i < caixas.Count; i++)
            {
                Vector3 agora = caixas[i].transform.position;

                float total = Vector3.Distance(agora, origem[i]);
                if (total > maxDeslocTotal)
                {
                    maxDeslocTotal = total;
                }

                float peloEmpurrao = Vector3.Distance(agora, antes[i]);
                if (peloEmpurrao > maxDeslocEmpurrao)
                {
                    maxDeslocEmpurrao = peloEmpurrao;
                }

                if (peloEmpurrao > 0.5f)
                {
                    movidas++;
                }

                if (agora.y < EscapeY)
                {
                    fugitivas++;
                }
            }

            double media = Media(custos);
            double p99 = Percentil(custos, 0.99);
            double maximo = Maximo(custos);

            Log(F("DESABAMENTO passos={0} t={1:F3} dormiu_no_passo={2} pico_acordados={3} " +
                  "no_passo={4} custo_ms_medio={5:F3} custo_ms_p99={6:F3} custo_ms_max={7:F3} " +
                  "desloc_pelo_empurrao_u={8:F4} desloc_total_u={9:F4} caixas_movidas={10} " +
                  "fugitivas={11}",
                steps, steps * dt, passoDormiu, picoAcordados, passoPico,
                media, p99, maximo, maxDeslocEmpurrao, maxDeslocTotal, movidas, fugitivas));

            // O orcamento de quadro a 60 fps e 16.67 ms. Este numero e SO a fisica, sem
            // render, sem scripts e sem rede — e piso, nao gasto. Fica no log como fracao
            // do orcamento justamente para ninguem confundir os dois.
            Log(F("DESABAMENTO fisica_p99_como_fracao_de_16.67ms={0:F3} ({1:F1}%)",
                p99 / 16.67, p99 / 16.67 * 100.0));

            if (fugitivas > 0)
            {
                Fail(F("{0} caixas atravessaram o chao no desabamento (y < {1:F2})",
                    fugitivas, EscapeY));
                return false;
            }

            if (passoDormiu == 0)
            {
                Fail(F("a pilha nao voltou a dormir em {0} passos ({1:F1} s); ainda {2} acordados",
                    StackMaxSteps, StackMaxSteps * dt, acordados));
                return false;
            }

            // "Quantas caixas um jogador derruba" NAO reprova a corrida, de proposito. E
            // numero a reportar, como a banda no contrato de medicao: inventar um minimo
            // seria eu decidindo quanta bagunca o jogo deve permitir, que e design.
            Log(F("DESABAMENTO leitura: um empurrao de escala de jogador ({0:F0} Ns, o " +
                  "momento de 70 kg a 4 u/s) moveu {1} de {2} caixas mais de 0.5 u",
                CollapseImpulse, movidas, caixas.Count));

            return true;
        }

        static double Media(List<double> valores)
        {
            double soma = 0;
            for (int i = 0; i < valores.Count; i++)
            {
                soma += valores[i];
            }

            return soma / valores.Count;
        }

        static double Maximo(List<double> valores)
        {
            double maior = valores[0];
            for (int i = 1; i < valores.Count; i++)
            {
                if (valores[i] > maior)
                {
                    maior = valores[i];
                }
            }

            return maior;
        }

        static double Percentil(List<double> valores, double fracao)
        {
            var copia = new List<double>(valores);
            copia.Sort();

            int indice = Mathf.Clamp(
                Mathf.CeilToInt((float)(fracao * copia.Count)) - 1, 0, copia.Count - 1);

            return copia[indice];
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
            Debug.Log("[PROBE] " + mensagem);
        }

        /// <summary>
        /// Registra a falha e DEIXA a execucao voltar. Nao encerra o processo aqui: sair
        /// de dentro do try pularia a restauracao do simulationMode (errors/02).
        /// </summary>
        static void Fail(string motivo)
        {
            if (failure == null)
            {
                failure = motivo;
            }
        }
    }
}
