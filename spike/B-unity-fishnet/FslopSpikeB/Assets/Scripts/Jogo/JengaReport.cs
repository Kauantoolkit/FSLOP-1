using System.Globalization;
using UnityEngine;

namespace Fslop.SpikeB.Jogo
{
    /// <summary>
    /// Imprime o estado das zonas uma vez por segundo, no mesmo formato de linha do resto do
    /// projeto. Existe para a sessao deixar rastro: "a pilha do jogador 2 chegou a 4.8 u e
    /// desabou aos 3 min" e uma frase que so se escreve com log.
    ///
    /// NAO julga. Nao ha vencedor, nao ha ponto, nao ha fim de rodada. So descreve.
    /// </summary>
    public class JengaReport : MonoBehaviour
    {
        [SerializeField] float intervalo = 1f;

        StackZone[] zonas;
        float relogio;
        double inicio;

        void Start()
        {
            zonas = FindObjectsByType<StackZone>();
            inicio = Time.realtimeSinceStartupAsDouble;

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[JENGA-META] zonas={0} iniciado={1}",
                zonas.Length, System.DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
        }

        void Update()
        {
            relogio += Time.unscaledDeltaTime;
            if (relogio < intervalo)
            {
                return;
            }

            relogio = 0f;
            float t = (float)(Time.realtimeSinceStartupAsDouble - inicio);

            foreach (var zona in zonas)
            {
                zona.Amostrar(out int corpos, out int empilhados, out float altura);

                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[JENGA] t={0:F1} zona={1} corpos={2} empilhados={3} altura_u={4:F2}",
                    t, zona.ZoneName, corpos, empilhados, altura));
            }
        }
    }
}
