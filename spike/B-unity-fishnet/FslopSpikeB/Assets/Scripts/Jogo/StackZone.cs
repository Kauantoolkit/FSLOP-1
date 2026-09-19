using System.Collections.Generic;
using UnityEngine;

namespace Fslop.SpikeB.Jogo
{
    /// <summary>
    /// Uma area no chao. Conta quantas caixas estao empilhadas dentro dela e qual a altura
    /// da pilha.
    ///
    /// O QUE ELA NAO FAZ, e e deliberado: nao declara vencedor, nao tem turno, nao tem
    /// ponto, nao impede ninguem de mexer na pilha de outro. Essas quatro coisas sao regra
    /// de jogo, e regra de jogo e do usuario — o briefing e explicito ("Voce nao decide
    /// design"). Ela MEDE, e o Jenga emerge de quem joga: se voce puxar a caixa de baixo da
    /// pilha do seu amigo e ela cair, a zona dele vai relatar que caiu. Ninguem precisou
    /// escrever "se cair, perdeu".
    ///
    /// E instrumento, no fim das contas — a mesma familia do avaliador do soak: descreve o
    /// que aconteceu e nao julga.
    /// </summary>
    public class StackZone : MonoBehaviour
    {
        [SerializeField] string zoneName = "zona";
        [SerializeField] float radius = 4f;

        /// <summary>
        /// Acima disto o corpo conta como "caiu" em vez de "empilhado". 0.6 u e pouco mais
        /// de meia caixa: um corpo deitado no chao da zona nao e pilha.
        /// </summary>
        [SerializeField] float restingHeight = 0.6f;

        readonly List<Rigidbody> dentro = new List<Rigidbody>();

        public string ZoneName => zoneName;

        public void Configure(string nome, float raio)
        {
            zoneName = nome;
            radius = raio;
        }

        /// <summary>
        /// Recolhe o estado atual da zona. Devolve quantos corpos estao nela, quantos estao
        /// acima do chao (a pilha de verdade) e a altura do mais alto.
        /// </summary>
        public void Amostrar(out int corpos, out int empilhados, out float altura)
        {
            dentro.Clear();
            corpos = 0;
            empilhados = 0;
            altura = 0f;

            // COLUNA, nao esfera. A primeira versao usava OverlapSphere(posicao, raio) com o
            // centro no chao — o que deixa de fora tudo acima de `raio`. Numa zona de 3.5 u,
            // uma torre de 4 u teria as caixas DO TOPO invisiveis para a medicao: justamente
            // as que importam quando alguem puxa a de baixo.
            //
            // A zona e uma area no CHAO; o que esta acima dela pertence a ela, em qualquer
            // altura. 20 u de meia-altura cobre qualquer pilha que a fisica aguente (o PhysX
            // nao segura mais que ~6 camadas soltas, docs/99 item 11).
            var centro = transform.position + Vector3.up * 20f;
            var meia = new Vector3(radius, 20f, radius);

            foreach (var col in Physics.OverlapBox(centro, meia))
            {
                var corpo = col.attachedRigidbody;
                if (corpo == null || corpo.GetComponent<CapsuleMotor>() != null)
                {
                    continue;
                }

                // A caixa de overlap e QUADRADA e a marca no chao e REDONDA. Sem este filtro,
                // um corpo no canto (ate raio*1.41) contaria estando visivelmente fora da
                // area — o medido nao bateria com o visto, e quem joga confia no visto.
                Vector3 d = corpo.worldCenterOfMass - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > radius * radius)
                {
                    continue;
                }

                // OverlapBox devolve um resultado por COLLIDER. Uma caixa com mais de um
                // colisor seria contada duas vezes, e a contagem publicada estaria errada
                // por um motivo invisivel.
                if (dentro.Contains(corpo))
                {
                    continue;
                }

                dentro.Add(corpo);
                corpos++;

                float y = corpo.worldCenterOfMass.y;
                if (y > restingHeight)
                {
                    empilhados++;
                }

                if (y > altura)
                {
                    altura = y;
                }
            }
        }
    }
}
