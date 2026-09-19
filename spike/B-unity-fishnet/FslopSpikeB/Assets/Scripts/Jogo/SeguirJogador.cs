using UnityEngine;

namespace Fslop.SpikeB.Jogo
{
    /// <summary>
    /// Diz a camera para seguir este objeto.
    ///
    /// Existe porque no spike quem aponta a camera e o SoakRunner, e a cena do brinquedo nao
    /// tem SoakRunner — ela nao tem rede, nem roteiro, nem medicao de contrato. Um componente
    /// de duas linhas no jogador resolve sem arrastar o soak inteiro para ca.
    /// </summary>
    public class SeguirJogador : MonoBehaviour
    {
        void Start()
        {
            var camera = FindAnyObjectByType<FollowCamera>();
            if (camera != null)
            {
                camera.Follow(transform);
            }
        }
    }
}
