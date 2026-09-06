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

            Jump(restY);
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
