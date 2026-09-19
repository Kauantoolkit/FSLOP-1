using UnityEngine;

namespace Fslop.SpikeB.Jogo
{
    /// <summary>
    /// Le a tecla de agarrar e chama o <see cref="Grabber"/>.
    ///
    /// EXISTE SEPARADO POR UM MOTIVO DE FRONTEIRA, e vale registrar porque eu errei antes:
    /// na primeira versao eu pus isto dentro do CapsuleController, que e arquivo do SPIKE.
    /// Com isso o spike passou a referenciar Assets/Scripts/Jogo/ — exatamente o acoplamento
    /// que decisions/12 proibe, e que torna falsa a frase "apague Jogo/ e o spike compila
    /// igual".
    ///
    /// Aqui a dependencia aponta na direcao certa: o brinquedo conhece o spike, o spike nao
    /// conhece o brinquedo.
    ///
    /// "Fire1" existe no Input Manager antigo por padrao (mouse esquerdo / ctrl), que e o
    /// que este projeto usa. Nao precisa de eixo novo em ProjectSettings.
    /// </summary>
    [RequireComponent(typeof(Grabber))]
    public class GrabberInput : MonoBehaviour
    {
        Grabber agarre;

        void Awake()
        {
            agarre = GetComponent<Grabber>();
        }

        void Update()
        {
            if (Input.GetButtonDown("Fire1"))
            {
                agarre.Toggle();
            }
        }
    }
}
