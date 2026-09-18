using UnityEngine;

namespace Fslop.SpikeB
{
    /// <summary>
    /// Camera que segue um alvo. INSTRUMENTO, nao mecanica.
    ///
    /// Existe por um motivo so: sem ela o teste minimo nao e OBSERVAVEL por uma pessoa. A
    /// cena tem camera fixa em (0, 6, -12), que serve para corrida automatizada — onde
    /// ninguem olha — e nao serve para alguem pegar o teclado e sentir o agarre.
    ///
    /// Isto NAO e camera de jogo: nao tem mouse, nao tem colisao com parede, nao tem
    /// suavizacao ajustavel. Essas tres coisas sao decisoes de sensacao, e sensacao nao e
    /// minha (briefing: "Voce nao decide design"). E um suporte de tripe que anda junto.
    ///
    /// Em corrida headless ela nao custa nada: sem alvo, o Update sai na primeira linha.
    /// </summary>
    public class FollowCamera : MonoBehaviour
    {
        [SerializeField] Vector3 offset = new Vector3(0f, 7f, -11f);

        Transform target;

        public void Follow(Transform newTarget)
        {
            target = newTarget;
        }

        void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            // Sem suavizacao de proposito. Camera suavizada ESCONDE o que esta task mede:
            // se a viga da um salto de 0.7 u, uma camera com lag disfarca o salto. O tripe
            // e rigido para o defeito aparecer inteiro.
            transform.position = target.position + offset;
            transform.rotation = Quaternion.LookRotation(target.position - transform.position);
        }
    }
}
