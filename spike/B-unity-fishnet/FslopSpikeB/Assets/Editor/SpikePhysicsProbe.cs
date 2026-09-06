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
    /// comportamento na candidata B, e e proposital que ela venha antes do controle da
    /// capsula — instrumento primeiro, teste depois (decisions/05).
    ///
    /// Nao salva a cena: os transforms sao movidos pela simulacao e o arquivo em disco
    /// tem que continuar sendo o que o builder escreveu.
    ///
    /// Rodar: Unity.exe -projectPath ... -batchmode -quit -nographics
    ///        -executeMethod Fslop.SpikeB.EditorTools.SpikePhysicsProbe.Run
    /// </summary>
    public static class SpikePhysicsProbe
    {
        const int MaxSteps = 600;              // 12 s a 50 Hz; a queda teorica leva ~0.9 s
        const float RestSpeedThreshold = 0.01f; // u/s
        const int RestStepsRequired = 10;       // parado por 10 passos seguidos = repouso
        const float ExpectedRestY = 1.0f;       // meia-altura da capsula, topo do chao em y=0
        const float RestToleranceU = 0.02f;     // margem para o contact offset do PhysX

        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(SpikeSceneBuilder.ScenePath, OpenSceneMode.Single);
            Log("cena=" + SpikeSceneBuilder.ScenePath + " raizes=" + scene.rootCount);

            var player = Find(scene, SpikeSceneBuilder.PlayerName);
            var ground = Find(scene, SpikeSceneBuilder.GroundName);
            var camera = Find(scene, SpikeSceneBuilder.CameraName);
            var sun = Find(scene, SpikeSceneBuilder.SunName);

            var body = player.GetComponent<Rigidbody>();
            if (body == null)
            {
                Fail("o objeto " + SpikeSceneBuilder.PlayerName + " nao tem Rigidbody");
                return;
            }

            float groundTopY = ground.transform.position.y + ground.transform.localScale.y * 0.5f;
            float dt = Time.fixedDeltaTime;

            Log(F("dt={0:F4} gravity_y={1:F3} mass_kg={2:F1} spawn_y={3:F4} ground_top_y={4:F4}",
                dt, Physics.gravity.y, body.mass, player.transform.position.y, groundTopY));
            Log(F("camera={0} luz={1}",
                camera.GetComponent<Camera>() != null ? "ok" : "SEM Camera",
                sun.GetComponent<Light>() != null ? "ok" : "SEM Light"));

            var modoAnterior = Physics.simulationMode;
            int steps = 0;
            int restStreak = 0;
            float minY = float.MaxValue;

            try
            {
                Physics.simulationMode = SimulationMode.Script;

                for (steps = 1; steps <= MaxSteps; steps++)
                {
                    Physics.Simulate(dt);

                    float y = player.transform.position.y;
                    float speed = body.linearVelocity.magnitude;
                    if (y < minY)
                    {
                        minY = y;
                    }

                    // Amostra rala so para o log mostrar a queda, nao para julgar nada.
                    if (steps <= 5 || steps % 25 == 0)
                    {
                        Log(F("step={0,3} t={1:F3} y={2:F4} vy={3:F4} speed={4:F4}",
                            steps, steps * dt, y, body.linearVelocity.y, speed));
                    }

                    restStreak = speed < RestSpeedThreshold ? restStreak + 1 : 0;
                    if (restStreak >= RestStepsRequired)
                    {
                        break;
                    }
                }
            }
            finally
            {
                Physics.simulationMode = modoAnterior;
            }

            if (restStreak < RestStepsRequired)
            {
                Fail(F("a capsula nao chegou ao repouso em {0} passos ({1:F2} s); y={2:F4} speed={3:F4}",
                    MaxSteps, MaxSteps * dt, player.transform.position.y, body.linearVelocity.magnitude));
                return;
            }

            float restY = player.transform.position.y;
            float erro = Mathf.Abs(restY - ExpectedRestY);
            float afundou = groundTopY - (restY - 1.0f); // quanto do corpo entrou no chao

            Log(F("REPOUSO passos={0} t={1:F3} y={2:F4} esperado={3:F4} erro_u={4:F4} penetracao_u={5:F4} min_y={6:F4}",
                steps, steps * dt, restY, ExpectedRestY, erro, afundou, minY));

            if (erro > RestToleranceU)
            {
                Fail(F("repouso fora da tolerancia: erro_u={0:F4} > {1:F4}", erro, RestToleranceU));
                return;
            }

            Log("PASS");
            EditorApplication.Exit(0);
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
            return null; // Exit ja encerrou o processo; o return existe para o compilador.
        }

        static string F(string formato, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, formato, args);
        }

        static void Log(string mensagem)
        {
            Debug.Log("[PROBE] " + mensagem);
        }

        static void Fail(string motivo)
        {
            Debug.LogError("[PROBE] FAIL " + motivo);
            EditorApplication.Exit(1);
        }
    }
}
